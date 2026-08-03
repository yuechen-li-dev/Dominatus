# Dominatus versus conventional robotics control

## 1. Thesis

M7 compares programming models, not control-theory legitimacy. Dominatus does not replace control theory. It gives control theory a coherent software execution model for realistic systems.

Frontier LLMs should not spend their lives manually remote-controlling machines one actuator command at a time. They should design, inspect, and adapt control systems. Dominatus is the kernel that executes those systems quickly, persistently, and transparently.

## 2. Shared plant and scenario contract

Both controllers implement `IQuadcopterController.Update(QuadcopterObservation, dt)` and drive the same `QuadcopterPlant`, `SensorSuite`, `MotorMixer`, `MiniSmithPredictor`, `FaultScenarioRunner`, DTOs, timing, initial states, disturbances, authority limits, and criteria. Shared code is 324 physical source lines and is charged to neither side.

The plant tracks roll/pitch/yaw and p/q/r, unequal axis response, Euler-style cross-axis terms, aerodynamic damping, a delayed motor-command queue, external torque, motor clamping, arming, saturation time, and front-left authority loss. Translation, propeller aerodynamics, battery state, contact, and ground effect are omitted.

Scenarios cover both initial-error signs, coupled disturbance, yaw, IMU dropout, vision dropout, sensor conflict, actuator degradation, wind, 100 ms delay, saturation, emergency disarm, unknown anomaly, and the later camera-latency change.

## 3. Dominatus architecture

The generated `robotics.quadcopter3d.control` flow has 19 authored durable states: `Boot`, `Disarmed`, `Arming`, `NominalControl`, `CorrectAttitude`, `BrakeAngularRate`, `HoldAttitude`, `SensorDegraded`, `SensorConflict`, `VisionOperation`, `VisionRecovery`, `ImuRecovery`, `ActuatorDegraded`, `WindRecovery`, `SafeHover`, `ControlledDescent`, `EmergencyStop`, `EscalateNovelCondition`, and `TestComplete`.

Numerical feedback lives inside the control states. Utility chooses regimes. The HFSM preserves recovery meaning. Typed operations represent motor dispatch, delayed OpenCV processing, and bounded LLM scoring. Generated construction contributes 53 lines and is reported separately, not as handwritten authoring.

## 4. Conventional architecture

The baseline is an honest componentized C# analogue of common PX4/ROS 2 separation, not PX4 source and not a ROS 2 port. It contains `ImuSubscriber`, `VisionSubscriber`, `MagnetometerSubscriber`, `AttitudeEstimator`, `HealthMonitor`, `FaultRecoveryCoordinator`, `ModeManager`, `AttitudeController`, `ActuatorPublisher`, `DiagnosticsPublisher`, and `LlmEscalationCoordinator`.

It uses no Dominatus assembly. Callback delivery is deterministic in simulation, and one conceptual 50 Hz timer owns estimation, health evaluation, mode application, numerical control, publication, and diagnostics.

## 5. Sensor fusion and confidence handling

Both use the same bounded fusion rule. Healthy recent IMU dominates short-term attitude; confident vision corrects roll/pitch drift at an 18% weight; magnetometer supplies yaw. One available sensor supports degraded control. Disagreement above 12 degrees is not averaged: the controller trusts the healthy inertial path temporarily and enters an explicit conflict mode. Both missing attitude sources select reduced-authority fallback.

IMU samples carry noise, accumulating bias, spike/dropout status, sequence, timestamp, and confidence. Vision carries sequence, timestamp, confidence, latency/dropout status, and roll/pitch. Magnetometer confidence affects yaw.

## 6. Delay compensation

`MiniSmithPredictor` projects attitude to the known actuation horizon from current attitude/rate and average pending motor moment under a small approximate angular model. It is deterministic and intentionally described as a mini predictor, not a complete formal Smith predictor.

On the shared 100 ms-delay case, both predictor-enabled controllers recovered. A conventional enabled/disabled test holds the plant constant and verifies predictor overshoot is no worse (within 0.25 degrees). One representative run settled the enabled Dominatus path at 3.64 s and the enabled conventional path at 3.66 s. No formal stability conclusion follows.

## 7. Fault and recovery handling

Emergency disarm has utility score 1.0 and zeroes all motors. Sensor dropouts select known degraded strategies without an LLM. Persistent sensor conflict uses reduced authority until sources reconcile. Known front-left authority loss is compensated explicitly before plant saturation; excessive predicted error can route to controlled descent. Wind and coupled impulses remain ordinary feedback cases. Sustained disturbance saturates but no integrator exists to wind up.

## 8. LLM escalation boundary

Routine stabilization, wind, both known sensor dropouts, sensor conflict, actuator degradation, delay, saturation, and camera latency produced zero LLM calls. An unknown anomaly must persist for 0.8 s, then enter `SafeHover`. Only then may `EscalateNovelCondition` invoke `Llm.Decide` with `reduce_aggressiveness`, `controlled_descent`, and `abort`. The deterministic fake selected controlled descent once.

The conventional controller provides the equivalent safe-mode and bounded fake-decision coordinator. Neither LLM path accepts or emits motor values, and neither runs at 50 Hz.

## 9. Control results

Representative Release/net8.0 deterministic run, 900 ticks at 20 ms:

| Scenario | Dominatus settle / degraded / LLM | Conventional settle / degraded / LLM | Result |
| --- | ---: | ---: | --- |
| Nominal (14°, -10°, 7°) | 3.66 s / 0 / 0 | 3.66 s / 0 / 0 | both recovered |
| Coupled disturbance | 5.16 s / 0 / 0 | 5.16 s / 0 / 0 | both recovered |
| IMU dropout | 3.70 s / 3.58 s / 0 | 3.72 s / 3.60 s / 0 | both recovered |
| Vision dropout | 3.54 s / 3.58 s / 0 | 3.70 s / 3.60 s / 0 | both recovered |
| Sensor conflict | 3.86 s / 5.98 s / 0 | 4.12 s / 6.00 s / 0 | both recovered |
| Actuator degradation | 3.44 s / 14.98 s / 0 | 3.50 s / 15.00 s / 0 | both recovered |
| Saturation | 14.60 s / 0 / 0 | 14.60 s / 0 / 0 | both recovered; ~5.65 s saturated |
| Unknown anomaly | 3.66 s / safe route / 1 | 3.66 s / 14.18 s / 1 | bounded descent selected |
| Camera latency spike | 3.62 s / 0.14 s / 0 | 3.68 s / 0.16 s / 0 | both recovered |

Peak nominal errors equal the initial errors and nominal overshoot is zero. Under the deliberately sustained saturation torque, peak roll reached about 82 degrees; recovery after removal satisfies the bounded fixture criterion but is not evidence of flight safety.

Approximate end-to-end test timing was 59–157 ms per Dominatus scenario and 2.5–16 ms per conventional scenario on the development machine. Allocations were roughly 39–54 MB versus 2.5 MB. OpenCV encoding, operation/state scheduling, and copied trace snapshots dominate the Dominatus measurement. These are test-harness observations, not hard-real-time benchmarks. M8 records this as a diagnostic-trace-heavy baseline, preserves full trace capability, and intentionally defers an allocation redesign.

## M8 closeout

The sample is a completed simulation experiment, not production flight software, certification evidence, formal stability analysis, or a claim to replace control theory. The conventional implementation was shorter and faster in this fixture; Dominatus demonstrated stateful recovery, typed obligations, inspection, and bounded LLM escalation above the loop. Further robotics work is paused; see [deferred work](DOMINATUS_ROBOTICS_DEFERRED_WORK.md).

## 10. Programming-model measurements

Physical lines were classified by primary responsibility; mixed lines make category splits approximate, while file totals are exact.

| Metric | Dominatus | Conventional |
| --- | ---: | ---: |
| Handwritten side-exclusive source | 435 | 208 |
| Continuous control mathematics | ~23 | ~24 |
| Estimation/fusion | ~38 | ~30 |
| Domain policy, modes, recovery, faults | ~211 | ~66 |
| Operations/actuation/LLM plumbing | ~93 | ~30 |
| Callback/message/synchronization plumbing | ~25 | ~42 |
| Trace/diagnostics | ~35 | ~16 |
| Explicit authored states/modes | 19 | 9 |
| Current-mode representations | 2 (HFSM path + diagnostic key) | 3 (pending request + mode + diagnostic snapshot) |
| Independently synchronized mutable stores | 2 principal stores (agent BB/HFSM + adapter estimator/history) | 8 component stores |
| Callbacks/event handlers | operation/event runtime; 0 authored callbacks | 6 authored callbacks |
| Timers | 1 simulation cadence | 1 control-loop cadence |
| Message/command DTOs | 2 side-specific typed commands | fault/mode requests plus shared DTOs |
| Manual recovery-routing branches | 2 bounded LLM result routes; state transitions otherwise authored | ~9 switch/if routes |
| Explicit external-operation contracts | 3 | 0 runtime contracts; 2 coordinator/publisher interfaces |
| Test source | 268 shared/cross-controller lines | same shared test project |
| Generated source | 53 (excluded above) | 0 |

Raw LOC does not favor Dominatus in this small fixture: explicit policy and operation semantics cost source. Its stronger result is fewer independently synchronized representations of control state. The conventional implementation is shorter and much faster here; Dominatus concentrates temporal meaning and inspection in a generated flow rather than proving a universal size or throughput advantage.

## 11. Change-amplification experiment

`CameraLatencySpike` was added after parity. Exact footprint: shared `Scenarios.cs` added the scenario and injection; `DominatusControllerTests.cs` and `ConventionalAndComparisonTests.cs` each added the case. Neither controller implementation changed because both already classified stale vision as a known degraded condition. Both recovered with zero LLM calls. This experiment measures extension of an existing fault class, not addition of a novel recovery mode.

## 12. What Dominatus simplifies

Dominatus centralizes durable state identity, utility arbitration, operation identity, pending-operation replay, bounded LLM routing, and state inspection. A new genuinely distinct fault policy can be expressed as a state and utility option without adding another callback topology or mode enum translation at every layer.

## 13. What Dominatus does not replace

It does not replace sensor physics, estimator design, control-law tuning, rigid-body dynamics, geometric control, MPC, Kalman filtering, actuator characterization, real-time scheduling, verification, or safety engineering. Mature flight software remains the appropriate production reference.

## 14. Threats to validity

This is one deterministic attitude-only simulation, one tuning, one machine, one conventional decomposition, and no randomized campaign. Both controllers share mathematical helpers, which improves fairness but reduces implementation diversity. Metrics include test and OpenCV overhead. Recovery thresholds are fixture criteria, not aviation requirements. The change experiment reused an existing fault class.

## 15. Production limitations

There is no translation, quaternion/SO(3) attitude, motor/propeller model, battery, altitude controller, collision model, hardware timing, process isolation, native Linux OpenCV runtime package, formal stability proof, fault-tree analysis, watchdog certification, or physical vehicle. The current pending-replay path resumes without duplicate dispatch, but replay-injected completion leaves a stale restored in-flight bookkeeping entry. No live LLM provider is used.

## 16. Conclusion

Both architectures control the same plant and recover from the same bounded scenarios. The conventional controller is compact and efficient. Dominatus makes policy, temporal recovery, external obligations, and the LLM boundary unusually explicit and inspectable. That is the demonstrated thesis: numerical control below, bounded utility and state execution in the kernel, and general LLM reasoning only above the fast loop when authored recovery no longer classifies the situation.
