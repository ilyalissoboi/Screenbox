using System;
using SendAirPlay2;
using Windows.Security.Credentials;

namespace Screenbox.Core.Casting.AirPlay;

/// <summary>
/// Keeps send-airplay2 pairing credentials in the app's PasswordVault: one
/// credential per profile under the resource <see cref="Resource"/>, with the
/// record Base64-encoded because the vault holds strings.
/// </summary>
/// <remarks>
/// A record contains the controller's private signing key, so it belongs only in
/// the vault, never in settings, files or logs. The vault holds at most 20
/// credentials per app and may roam with the user's account; both are acceptable
/// for a handful of TVs. Base64 strings cannot be wiped from managed memory.
/// The library calls these methods on its own thread during pairing or a cast.
/// </remarks>
internal sealed class PasswordVaultCredentialStore : ICredentialStore
{
    internal const string Resource = "Screenbox.AirPlay";

    // HRESULT_FROM_WIN32(ERROR_NOT_FOUND), from Retrieve for a missing credential.
    private const int ElementNotFound = unchecked((int)0x80070490);

    private readonly PasswordVault _vault = new();

    /// <summary>Whether the vault holds credentials for <paramref name="profile"/>.</summary>
    internal bool HasProfile(string profile)
    {
        return Find(profile) is not null;
    }

    public CredentialStoreResult Load(string profile, byte[] buffer, out int length)
    {
        length = 0;
        PasswordCredential? credential = Find(profile);
        if (credential is null)
        {
            return CredentialStoreResult.Absent;
        }

        credential.RetrievePassword();
        byte[] record = Convert.FromBase64String(credential.Password);
        try
        {
            if (record.Length > buffer.Length)
            {
                return CredentialStoreResult.Unavailable;
            }

            Array.Copy(record, buffer, record.Length);
            length = record.Length;
            return CredentialStoreResult.Ok;
        }
        finally
        {
            Array.Clear(record);
        }
    }

    public CredentialStoreResult SaveNew(string profile, byte[] record)
    {
        if (Find(profile) is not null)
        {
            return CredentialStoreResult.Exists;
        }

        _vault.Add(new PasswordCredential(Resource, profile, Convert.ToBase64String(record)));
        return CredentialStoreResult.Ok;
    }

    public CredentialStoreResult Erase(string profile)
    {
        PasswordCredential? credential = Find(profile);
        if (credential is null)
        {
            return CredentialStoreResult.Absent;
        }

        _vault.Remove(credential);
        return CredentialStoreResult.Ok;
    }

    private PasswordCredential? Find(string profile)
    {
        try
        {
            return _vault.Retrieve(Resource, profile);
        }
        catch (Exception e) when (e.HResult == ElementNotFound)
        {
            return null;
        }
    }
}
