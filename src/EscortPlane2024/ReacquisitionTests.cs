namespace EscortPlane2024;

internal static class ReacquisitionTests
{
    public static void Run(Diagnostics log, Action<bool, string> check)
    {
        var now = DateTimeOffset.UtcNow;
        var settings = new FormationSettings(MinIas: 150, MaxIas: 350, VerticalSpeed: true);
        FollowerTests.Session session = null!;
        FollowerController controller = null!;
        void Start()
        {
            session = new(now);
            controller = new(session, log) { Settings = settings, ResumeWhenTelemetryReturns = true };
            controller.Engage(now); controller.Tick(now);
        }
        void Recover(int from = 8)
        {
            for (var i = from; i < from + 4; i++)
            { session.Refresh(now.AddSeconds(i), i); controller.Tick(now.AddSeconds(i)); }
        }
        Start(); var count = session.Outputs.Count;
        session.Refresh(now.AddSeconds(6), 6, false); controller.Tick(now.AddSeconds(6));
        check(!controller.Active && controller.WaitingForTelemetry && session.Outputs.Count == count,
            "Enabled reacquisition waits without commands after a target telemetry timeout");
        session.Refresh(now.AddSeconds(8), 8); controller.Tick(now.AddSeconds(8));
        check(controller.WaitingForTelemetry && !controller.Active && session.Outputs.Count == count,
            "One returning position cannot restart following before motion is established");
        for (var i = 9; i <= 11; i++) { session.Refresh(now.AddSeconds(i), i); controller.Tick(now.AddSeconds(i)); }
        check(controller.Active && !controller.WaitingForTelemetry && session.Outputs.Count > count,
            "Consistent fresh motion automatically resumes the same selected lead");

        Start(); count = session.Outputs.Count;
        controller.TelemetryLost("Relay disconnected"); session.Focus.ResetMotion();
        check(session.Focus.TargetId == 123 && session.Focus.Latest == null,
            "Discarding stale relay motion retains the exact selected identity");
        session.Refresh(now.AddSeconds(6), 6, false); controller.Tick(now.AddSeconds(6));
        check(controller.WaitingForTelemetry && session.Outputs.Count == count,
            "Relay outage with no positions remains waiting without commands");
        Recover(); check(controller.Active, "Relay recovery reacquires identity after motion history was cleared");

        Start(); controller.TelemetryLost("No data"); session.Focus.ResetMotion();
        controller.Stop("Pilot stop"); count = session.Outputs.Count; Recover();
        check(!controller.Active && !controller.WaitingForTelemetry && session.Outputs.Count == count,
            "Pilot stop cancels a pending automatic restart");

        Start(); controller.TelemetryLost("No data"); controller.ResumeWhenTelemetryReturns = false;
        Recover(); check(!controller.Active && !controller.WaitingForTelemetry,
            "Disabling the resume switch cancels waiting immediately");

        Start(); controller.TelemetryLost("No data"); session.OwnTitle = "Different aircraft";
        Recover(); check(!controller.Active && !controller.WaitingForTelemetry,
            "Changing own aircraft cancels automatic resume");

        Start(); controller.TelemetryLost("No data"); session.Focus.LockIdentity(456, true);
        session.Refresh(now.AddSeconds(1), 1, false); controller.Tick(now.AddSeconds(1));
        check(!controller.Active && !controller.WaitingForTelemetry,
            "A different target ID cannot inherit the resume request");

        Start(); controller.TelemetryLost("No data");
        session.StandardState = session.StandardState! with { MachMode = true };
        Recover(); check(!controller.Active && !controller.WaitingForTelemetry,
            "Pilot changing IAS/Mach mode cancels automatic resume");

        Start(); controller.TelemetryLost("No data"); session.Focus.ResetMotion();
        session.StandardState = session.StandardState! with { VsMode = false };
        count = session.Outputs.Count; Recover();
        check(controller.WaitingForTelemetry && !controller.Active && session.Outputs.Count == count,
            "Fresh target data cannot bypass required stock V/S mode");
        session.StandardState = session.StandardState with { VsMode = true };
        session.Refresh(now.AddSeconds(12), 12); controller.Tick(now.AddSeconds(12));
        check(controller.Active, "Waiting resumes only after aircraft mode requirements are satisfied");

        Start(); controller.TelemetryLost("No data"); session.Focus.ResetMotion();
        session.Flight = session.Flight! with { OnGround = 1 }; count = session.Outputs.Count; Recover();
        check(controller.WaitingForTelemetry && !controller.Active && session.Outputs.Count == count,
            "Automatic resume cannot command a grounded own aircraft");

        Start(); count = session.Outputs.Count; controller.Tick(now.AddSeconds(3));
        check(controller.WaitingForTelemetry && !controller.Active && session.Outputs.Count == count,
            "Stale own telemetry also suspends outputs for reacquisition");
        Recover(); check(controller.Active, "Fresh own and target telemetry can resume together");

        session = new(now) { Accept = false };
        controller = new(session, log) { Settings = settings, ResumeWhenTelemetryReturns = true };
        controller.Engage(now);
        for (var i = 0; i < 14; i++) { session.Refresh(now.AddSeconds(i), i); controller.Tick(now.AddSeconds(i)); }
        check(!controller.Active && !controller.WaitingForTelemetry && controller.Status.Contains("not confirmed"),
            "Automatic reacquisition does not restart an unconfirmed autopilot command");

        session = new(now); controller = new(session, log) { Settings = settings, ResumeWhenTelemetryReturns = true };
        controller.TelemetryLost("No data"); Recover();
        check(!controller.Active && !controller.WaitingForTelemetry,
            "Enabling automatic resume alone never starts an unrequested follow");
    }
}
