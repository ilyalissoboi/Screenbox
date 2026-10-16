using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Screenbox.Core.Casting.AirPlay;
using Screenbox.Core.Contexts;
using Screenbox.Core.Enums;
using Screenbox.Core.Helpers;
using Screenbox.Core.Messages;
using Screenbox.Core.Models;
using Screenbox.Core.Playback;
using Screenbox.Core.Services;
using Screenbox.Core.Tests.Helpers;
using Screenbox.Core.ViewModels;

namespace Screenbox.Core.Tests.ViewModels;

public class CastControlViewModelTests
{
    [Test]
    [Arguments(RendererKind.Chromecast, true)]
    [Arguments(RendererKind.Chromecast, false)]
    [Arguments(RendererKind.AirPlay, true)]
    public async Task CastEligibility_WhileForgetting_ShouldBlockOtherwiseReadyRenderers(RendererKind kind, bool airPlayReady)
    {
        await Assert.That(CastControlViewModel.IsCastAllowed(kind, isForgetting: false, airPlayReady)).IsTrue();
        await Assert.That(CastControlViewModel.IsCastAllowed(kind, isForgetting: true, airPlayReady)).IsFalse();
    }

    [Test]
    public async Task Forget_WhenSuccessful_ShouldOfferPairAndKeepOtherPairings()
    {
        var first = CreateRenderer("AA:BB:CC:DD:EE:01");
        var second = CreateRenderer("AA:BB:CC:DD:EE:02");
        var service = new TestPairingService(first, second);
        var vm = CreateViewModel(service);
        vm.SelectedRenderer = first;

        await vm.ForgetCommand.ExecuteAsync(null);

        await Assert.That(vm.IsSelectedRendererUnpaired).IsTrue();
        await Assert.That(vm.PairCommand.CanExecute(null)).IsTrue();
        await Assert.That(vm.CastCommand.CanExecute(null)).IsFalse();
        await Assert.That(vm.ForgetCommand.CanExecute(null)).IsFalse();
        await Assert.That(service.IsPaired(second)).IsTrue();
    }

    [Test]
    public async Task Forget_WhenFailed_ShouldKeepPairedStateAndReportError()
    {
        var first = CreateRenderer("AA:BB:CC:DD:EE:01");
        var service = new TestPairingService(first) { Succeeds = false };
        var vm = CreateViewModel(service);
        vm.SelectedRenderer = first;
        var recipient = new object();
        NotificationKind? received = null;
        WeakReferenceMessenger.Default.Register<NotificationMessage>(recipient, (_, message) => received = message.Kind);
        try
        {
            await vm.ForgetCommand.ExecuteAsync(null);

            await Assert.That(vm.IsSelectedRendererUnpaired).IsFalse();
            await Assert.That(vm.IsForgetting).IsFalse();
            await Assert.That(service.IsPaired(first)).IsTrue();
            await Assert.That(vm.ForgetCommand.CanExecute(null)).IsTrue();
            await Assert.That(received).IsEqualTo(NotificationKind.AirPlayForgetFailed);
        }
        finally
        {
            WeakReferenceMessenger.Default.UnregisterAll(recipient);
        }
    }

    [Test]
    public async Task Forget_WhenSelectionChanges_ShouldNotMarkTheNewSelectionUnpaired()
    {
        var first = CreateRenderer("AA:BB:CC:DD:EE:01");
        var second = CreateRenderer("AA:BB:CC:DD:EE:02");
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new TestPairingService(first, second)
        {
            BeforeForget = () =>
            {
                entered.TrySetResult();
                if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Forget test release timed out");
            }
        };
        var vm = CreateViewModel(service);
        vm.SelectedRenderer = first;
        Task operation = vm.ForgetCommand.ExecuteAsync(null);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.That(vm.IsForgetting).IsTrue();
            await Assert.That(vm.CastCommand.CanExecute(null)).IsFalse();
            await Assert.That(vm.PairCommand.CanExecute(null)).IsFalse();
            await Assert.That(vm.ForgetCommand.CanExecute(null)).IsFalse();
            vm.SelectedRenderer = second;
        }
        finally
        {
            release.Set();
            await operation;
        }

        await Assert.That(vm.IsSelectedRendererUnpaired).IsFalse();
        await Assert.That(service.IsPaired(first)).IsFalse();
        await Assert.That(service.IsPaired(second)).IsTrue();
        await Assert.That(vm.IsForgetting).IsFalse();
    }

    [Test]
    [Arguments(true, false, false)]
    [Arguments(false, true, false)]
    [Arguments(false, false, true)]
    public async Task Forget_WhilePairingOrCasting_ShouldNotRemoveCredentials(bool pairing, bool starting, bool casting)
    {
        var first = CreateRenderer("AA:BB:CC:DD:EE:01");
        var service = new TestPairingService(first);
        var vm = CreateViewModel(service);
        vm.SelectedRenderer = first;
        vm.IsPairing = pairing;
        vm.IsCastStarting = starting;
        vm.IsCasting = casting;

        await Assert.That(vm.ForgetCommand.CanExecute(null)).IsFalse();
        await vm.ForgetCommand.ExecuteAsync(null);
        await Assert.That(service.ForgetCalls).IsEqualTo(0);
        await Assert.That(service.IsPaired(first)).IsTrue();
    }

    [Test]
    public async Task Forget_WhenUnpairedOrNotSelected_ShouldBeUnavailable()
    {
        var service = new TestPairingService();
        var vm = CreateViewModel(service);

        await Assert.That(vm.IsSelectedRendererPairedAirPlay).IsFalse();
        await Assert.That(vm.ForgetCommand.CanExecute(null)).IsFalse();
        vm.SelectedRenderer = CreateRenderer("AA:BB:CC:DD:EE:01");
        await Assert.That(vm.IsSelectedRendererPairedAirPlay).IsFalse();
        await Assert.That(vm.ForgetCommand.CanExecute(null)).IsFalse();
    }

    private static Renderer CreateRenderer(string id) => new(new AirPlayReceiverInfo(id, "Test device", "192.0.2.1", 7000));

    private static CastControlViewModel CreateViewModel(IAirPlayPairingService pairing)
    {
        var player = new PlayerContext();
        var cast = new CastContext();
        var coordinator = new AirPlayCastCoordinator(player, cast, new TestPlayerService(), new TestSettingsService(),
            NullLogger<AirPlayCastCoordinator>.Instance);
        return new CastControlViewModel(player, cast, new TestCastService(), pairing,
            new TestPinDialogService(), coordinator, NullLogger<CastControlViewModel>.Instance);
    }

    private sealed class TestPairingService(params Renderer[] paired) : IAirPlayPairingService
    {
        private readonly HashSet<Renderer> _paired = [.. paired];
        public bool Succeeds { get; init; } = true;
        public Action? BeforeForget { get; init; }
        public int ForgetCalls { get; private set; }
        public bool IsPaired(Renderer renderer) => _paired.Contains(renderer);
        public Task<AirPlayPairingResult> PairAsync(Renderer renderer, Func<Task<string?>> requestPin) =>
            Task.FromResult(AirPlayPairingResult.Cancelled);
        public bool Forget(Renderer renderer)
        {
            BeforeForget?.Invoke();
            ForgetCalls++;
            if (!Succeeds) return false;
            _paired.Remove(renderer);
            return true;
        }
    }

    private sealed class TestPinDialogService : IAirPlayPinDialogService
    {
        public Task<string?> RequestPinAsync(string receiverName) => Task.FromResult<string?>(null);
    }

    private sealed class TestCastService : ICastService
    {
        public RendererWatcher CreateRendererWatcher(IMediaPlayer player) => throw new NotSupportedException();
        public AirPlayReceiverWatcher CreateAirPlayReceiverWatcher() => throw new NotSupportedException();
        public bool SetActiveRenderer(IMediaPlayer player, Renderer? renderer) => throw new NotSupportedException();
    }
}
