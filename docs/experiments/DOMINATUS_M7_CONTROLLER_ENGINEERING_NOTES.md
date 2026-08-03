# M7 controller engineering notebook

These notes were recorded as the shared plant and both controllers converged. They describe implementation observations, not reconstructed marketing claims.

## Shared plant and sensing

- The attitude-only plant needed unequal axis gains and explicit Euler-style rate coupling; three independent copies of the old roll fixture would not exercise coordination.
- Command delay belongs in the plant queue. Keeping it out of the controller made the predictor optional and allowed the same delayed path to test both controllers.
- A damaged front-left motor introduces roll, pitch, and yaw bias even at equal collective. Merely reducing gains did not recover; both controllers needed the same explicit measured-authority rebalance before saturation.
- Deterministic sinusoidal sensor noise made failures repeatable without hiding a random seed in the harness. IMU bias still drifts slowly, vision is delayed independently, and magnetometer confidence matters for yaw.
- OpenCV is real: a synthetic horizon is rendered, Canny edges and probabilistic Hough lines recover roll/pitch, and the operation returns a bounded primitive string. The Windows runtime package is pinned because this repository is validated on Windows.

## Dominatus controller

- Local numerical correction fits naturally inside persistent states. Trying to make utility arbitration itself produce every motor value would have obscured the control mathematics.
- A Dominatus operation consumes dispatch and completion across scheduler advances. The adapter therefore uses eight bounded micro-steps inside each 20 ms simulated plant period; this is simulation scheduling, not a hard-real-time claim.
- Repeating an immediate operation in a state requires an authored wait. Without it, the iterator can consume immediate completions and redispatch indefinitely inside one scheduler tick. Adding the wait made the timing assumption explicit.
- Utility decision memory intentionally suppresses a switch to the already-selected option. Control states therefore persist and issue commands while their regime guard remains true, returning to arbitration only when observations invalidate the regime.
- The delayed vision operation can be captured while pending, restored into a fresh world, completed through replay, and resumed without redispatch. Core currently retains the restored in-flight bookkeeping entry after a replay-injected completion; M7 verifies the important no-duplicate and state-resumption behavior and records this cleanup limitation rather than changing the save format.
- Safety dominance uses named score bands: emergency `1.00`, unknown-anomaly safe hover `0.98`, actuator degradation `0.92`, sensor conflict `0.88`, sensor dropout `0.78`, and vision servicing `0.70`, above ordinary correction. Hysteresis is `0.03` and minimum commitment is 40 ms.
- The LLM path is absent from every fast state. `EscalateNovelCondition` can run only after `SafeHover`, invokes the repository's real `Llm.Decide` step with a deterministic fake client, and routes the bounded choice to `SafeHover` or `ControlledDescent`.

## Conventional controller

- The conventional design was kept competent and compact: three subscribers, estimator, health monitor, recovery coordinator, mode manager, numerical controller, actuator publisher, diagnostics publisher, and LLM escalation coordinator.
- Deterministic callback order in the fixture is IMU, vision, magnetometer, estimator reconciliation, health propagation, mode update, control timer, actuator publication, diagnostics. A production executor would need timestamp queues and synchronization around these mutable stores.
- Mode intent naturally appeared in multiple places: a pending recovery request, the mode manager's current mode/reason, and the diagnostics snapshot. These are reasonable components, but programmers must keep them coherent.
- Equivalent LLM safety required manual routing across anomaly duration, mode manager, escalation coordinator, and the control-loop switch. The fake LLM still chooses only authored policies.

## Tuning and observed failures

- Initial attitude gain `0.018` with rate gain `0.006` crossed the tolerance quickly but produced a slow delayed oscillation in the Dominatus scheduled path. Using attitude `0.012` and rate damping `0.018` stabilized both signs and was then applied to the conventional controller for fairness.
- Treating zero motor output after disarm as saturation polluted the metric. Saturation time now counts only while armed.
- The front-left authority scenario initially settled around a large biased attitude. Applying the same inverse measured-authority compensation to both controllers removed the bias without hiding it in the plant.
- The measured Dominatus path allocates far more than the conventional path (roughly 39–54 MB versus 2.5 MB per 900-tick scenario in one Release test run). Most measured pressure comes from inspection trace snapshots, operation machinery, and repeated OpenCV frame encoding. This is actionable evidence, not a hard-real-time benchmark.

## Change-amplification experiment

After parity, `CameraLatencySpike` was added without redesigning either controller.

- Shared changes: `Scenarios.cs` gained the scenario identity and deterministic alternating eight-tick latency injection.
- Dominatus controller changes: none; the existing confidence/staleness arbitration selected `SensorDegraded`/`VisionRecovery`. `DominatusControllerTests.cs` added the case.
- Conventional controller changes: none; the existing subscriber/health/recovery path handled unhealthy delayed samples. `ConventionalAndComparisonTests.cs` added the case.
- Result: both remained bounded, invoked no LLM, and spent approximately 0.14–0.16 seconds in degraded mode. The small footprint reflects that latency mapped to an already-authored known-fault class; a genuinely new recovery policy would touch more conventional routing sites.
