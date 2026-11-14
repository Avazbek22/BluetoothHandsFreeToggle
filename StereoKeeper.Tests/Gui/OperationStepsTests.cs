using StereoKeeper.App;
using StereoKeeper.Core;
using StereoKeeper.Gui.ViewModels;
using Xunit;
using static StereoKeeper.Tests.Gui.Fixtures;

namespace StereoKeeper.Tests.Gui;

public sealed class OperationStepsTests
{
    private const string Gateway = "Bluetooth Audio Gateway (HFP)";
    private const string HandsFree = "Bluetooth Hands-Free Service (HFP)";

    [Fact]
    public void SoftResetPlansAStopAndStartForEveryRunningService()
    {
        var steps = new OperationSteps(OperationKind.Soft, Services());

        Assert.Equal(
            [StepKind.Stop, StepKind.Start, StepKind.Stop, StepKind.Start, StepKind.Verify],
            steps.Items.Select(item => item.Kind));
        Assert.Equal("Resetting call mode (1 of 2)", steps.Items[0].Title);
        Assert.Equal("Starting headphone sound again (2 of 2)", steps.Items[3].Title);
        Assert.Equal("Checking that stereo is back", steps.Items[4].Title);
        Assert.All(steps.Items, item => Assert.True(item.IsPending));
    }

    [Fact]
    public void SoftResetSkipsStoppedAndDisabledServicesAndUsesPlainTitlesForOne()
    {
        var services = new List<ServiceSnapshot>
        {
            Service("BthHFSrv", ServiceRunState.Stopped),
            Service("BTAGService"),
            Service("Other", ServiceRunState.Stopped, ServiceStartType.Disabled)
        };

        var steps = new OperationSteps(OperationKind.Soft, services);

        Assert.Equal([StepKind.Stop, StepKind.Start, StepKind.Verify], steps.Items.Select(item => item.Kind));
        Assert.Equal("Resetting call mode", steps.Items[0].Title);
    }

    [Fact]
    public void HardModeSavesStateThenDisablesAndStopsEachService()
    {
        var steps = new OperationSteps(OperationKind.Hard, Services());

        Assert.Equal(
            [StepKind.Prepare, StepKind.Disable, StepKind.Stop, StepKind.Disable, StepKind.Stop, StepKind.Verify],
            steps.Items.Select(item => item.Kind));
        Assert.Equal("Remembering how it was", steps.Items[0].Title);
        Assert.Equal("Blocking call mode (1 of 2)", steps.Items[1].Title);
        Assert.Equal("Turning off the headphone microphone (2 of 2)", steps.Items[4].Title);
        Assert.Equal("Checking that stereo is back", steps.Items[5].Title);
    }

    [Fact]
    public void RestoreReadsTheBackupThenRestoresAndStartsEachService()
    {
        var steps = new OperationSteps(OperationKind.Restore, Services(ServiceRunState.Stopped, ServiceStartType.Disabled));

        Assert.Equal(
            [StepKind.Prepare, StepKind.Startup, StepKind.Start, StepKind.Startup, StepKind.Start, StepKind.Verify],
            steps.Items.Select(item => item.Kind));
        Assert.Equal("Loading the saved settings", steps.Items[0].Title);
        Assert.Equal("Allowing call mode again (1 of 2)", steps.Items[1].Title);
        Assert.Equal("Turning the headphone microphone back on (1 of 2)", steps.Items[2].Title);
        Assert.Equal("Checking that everything is back", steps.Items[5].Title);
    }

    [Fact]
    public void BeginActivatesTheFirstStepOnlyOnce()
    {
        var steps = new OperationSteps(OperationKind.Hard, Services());

        steps.Begin(TimeSpan.Zero);
        steps.Apply(new OperationEvent(OperationEventKind.Step, "", Step: OperationStep.Disabling, Service: HandsFree), TimeSpan.FromSeconds(1));
        steps.Begin(TimeSpan.FromSeconds(2));

        Assert.True(steps.Items[0].IsDone);
        Assert.Same(steps.Items[1], steps.Active);
    }

    [Fact]
    public void EventsMoveThroughThePlanAndFinishEarlierSteps()
    {
        var steps = new OperationSteps(OperationKind.Soft, Services());
        steps.Begin(TimeSpan.Zero);

        steps.Apply(new OperationEvent(OperationEventKind.Step, "", Step: OperationStep.Stopping, Service: HandsFree), TimeSpan.Zero);
        steps.Apply(new OperationEvent(OperationEventKind.Step, "", Step: OperationStep.Starting, Service: HandsFree), TimeSpan.Zero);
        steps.Apply(new OperationEvent(OperationEventKind.Step, "", Step: OperationStep.Stopping, Service: Gateway), TimeSpan.Zero);

        Assert.Equal(
            [StepState.Done, StepState.Done, StepState.Active, StepState.Pending, StepState.Pending],
            steps.Items.Select(item => item.State));
    }

    [Fact]
    public void WaitingUpdatesTheActiveStepDetailWithoutMovingOn()
    {
        var steps = new OperationSteps(OperationKind.Soft, Services());
        steps.Apply(new OperationEvent(OperationEventKind.Step, "", Step: OperationStep.Stopping, Service: HandsFree), TimeSpan.Zero);

        steps.Apply(
            new OperationEvent(OperationEventKind.Waiting, "", TimeSpan.FromSeconds(3.7), OperationStep.WaitingStop, HandsFree),
            TimeSpan.FromSeconds(4));
        steps.Apply(
            new OperationEvent(OperationEventKind.Warning, "", Step: OperationStep.NotResponding, Service: HandsFree),
            TimeSpan.FromSeconds(9));

        Assert.Same(steps.Items[0], steps.Active);
        Assert.Equal("Windows is not responding. Safely forcing a restart…", steps.Items[0].Detail);
    }

    [Fact]
    public void WaitingDetailShowsWholeSeconds()
    {
        var steps = new OperationSteps(OperationKind.Soft, Services());
        steps.Apply(
            new OperationEvent(OperationEventKind.Waiting, "", TimeSpan.FromSeconds(3.7), OperationStep.WaitingStop, HandsFree),
            TimeSpan.Zero);

        Assert.Equal("Waiting for Windows · 3 s", steps.Items[0].Detail);
    }

    [Fact]
    public void UnplannedStepIsInsertedBeforeTheCheck()
    {
        var steps = new OperationSteps(OperationKind.Soft, [Service("BTAGService")]);
        steps.Apply(new OperationEvent(OperationEventKind.Step, "", Step: OperationStep.Stopping, Service: Gateway), TimeSpan.Zero);

        steps.Apply(new OperationEvent(OperationEventKind.Step, "", Step: OperationStep.SettingStartup, Service: Gateway), TimeSpan.Zero);

        Assert.Equal(
            [StepKind.Stop, StepKind.Startup, StepKind.Start, StepKind.Verify],
            steps.Items.Select(item => item.Kind));
        Assert.Same(steps.Items[1], steps.Active);
        Assert.Equal("Allowing call mode again", steps.Items[1].Title);
    }

    [Fact]
    public void UnplannedServiceGetsNoPartNumber()
    {
        var steps = new OperationSteps(OperationKind.Soft, Services());

        steps.Apply(new OperationEvent(OperationEventKind.Step, "", Step: OperationStep.Stopping, Service: "Something else"), TimeSpan.Zero);

        Assert.Equal("Resetting call mode", steps.Active!.Title);
    }

    [Fact]
    public void CompleteFinishesEveryStep()
    {
        var steps = new OperationSteps(OperationKind.Soft, Services());
        steps.Begin(TimeSpan.Zero);

        steps.Complete(TimeSpan.FromSeconds(1));

        Assert.All(steps.Items, item => Assert.True(item.IsDone));
        Assert.Null(steps.Active);
        Assert.Equal(1, steps.EstimateProgress(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void ProgressKeepsMovingDuringLongWaitsButNeverClaimsToBeFinished()
    {
        var steps = new OperationSteps(OperationKind.Soft, [Service("BTAGService")]);
        steps.Begin(TimeSpan.Zero);

        var values = Enumerable.Range(0, 120)
            .Select(second => steps.EstimateProgress(TimeSpan.FromSeconds(second)))
            .ToList();

        Assert.Equal(values.Order(), values);
        Assert.True(values[1] > values[0]);
        Assert.True(values[^1] < 1.0 / 3);
    }

    [Fact]
    public void ProgressNeverGoesBackwardsWhenAStepIsInserted()
    {
        var steps = new OperationSteps(OperationKind.Soft, [Service("BTAGService")]);
        steps.Apply(new OperationEvent(OperationEventKind.Step, "", Step: OperationStep.Starting, Service: Gateway), TimeSpan.Zero);
        var before = steps.EstimateProgress(TimeSpan.FromSeconds(5));

        steps.Apply(new OperationEvent(OperationEventKind.Step, "", Step: OperationStep.SettingStartup, Service: Gateway), TimeSpan.FromSeconds(5));

        Assert.True(steps.EstimateProgress(TimeSpan.FromSeconds(5)) >= before);
    }
}
