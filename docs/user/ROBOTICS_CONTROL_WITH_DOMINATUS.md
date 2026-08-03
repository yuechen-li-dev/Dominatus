# Quadcopter control authored with Dominatus

The `Dominatus.Robotics.Quadcopter` fixture asks a deliberately provocative question: what does a feedback controller look like when the control law itself is authored as an explicit Dominatus flow instead of as one traditional PID or nonlinear equation?

This is a simulation-only architectural experiment. Dominatus directly chooses the control regime and emits a normalized collective/roll motor mix to a deterministic roll-axis plant. There is no separate PID or nonlinear controller underneath the flow.

```text
simulated roll angle and roll rate
    ↓ blackboard observations
Dominatus utility arbitration
    ↓ explicit control regime
authored correction / braking / hold state
    ↓ typed MotorMixCommand
simulated quadcopter roll plant
    ↓ next roll angle and roll rate
```

## Traditional PID and nonlinear control

A roll-axis PID controller typically calculates a command on every sample:

```text
error = desiredRoll - measuredRoll
torque = Kp·error + Ki·∫error·dt + Kd·d(error)/dt
```

A nonlinear geometric quadcopter controller instead works from the vehicle model and orientation on SO(3). In broad form it computes attitude and angular-velocity errors, adds feed-forward and gyroscopic terms, and produces a continuous body-moment vector:

```text
M = -KR·eR - KΩ·eΩ + Ω×JΩ + model/feed-forward terms
```

Both approaches encode control primarily as a compact numerical law. Mode changes, saturation, arming, and fault handling normally surround that law in separate control logic.

## The Dominatus-authored alternative

The fixture expresses roll stabilization as a hybrid controller with inspectable states:

- `CorrectPositiveRoll` and `CorrectNegativeRoll` apply angle-dependent counter-torque.
- `BrakePositiveRate` and `BrakeNegativeRate` damp residual angular motion.
- `HoldLevel` applies a small rate-damping command inside the level band.
- `Disarmed` dominates arbitration and emits zero collective and zero torque.

Utility considerations select a regime from measured angle and angular rate. The selected state computes a bounded motor-mix command, sends it through the explicit `quad.control.apply-motor-mix` operation site, waits one authored control period, and reevaluates when its guard no longer holds. In other words, Dominatus is the controller here; the plant adapter only integrates the toy dynamics.

| Property | PID / nonlinear law | Dominatus fixture |
| --- | --- | --- |
| Primary representation | Continuous equation | Explicit hybrid states and utility policy |
| Control output | Torque/motor command | Typed `MotorMixCommand` |
| Switching behavior | Usually surrounding logic | First-class authored states |
| Inspection | Gains, errors, numerical logs | Active mode, scores, state, pending command |
| Persistence | Usually controller-specific | Explicit operation/state boundaries |
| Mathematical guarantees | Established analysis methods may apply | Not established by this fixture |
| Timing overhead | Suitable for tight embedded loops | Current runtime is not claimed hard real-time |

This comparison is not a claim that the authored hybrid policy outperforms geometric control, MPC, or a tuned PID. The one-axis plant omits coupling, motor lag, estimator noise, saturation dynamics, translation, and disturbances. The useful evidence is narrower: ordinary Dominatus APIs can author a deterministic closed feedback loop that stabilizes the bounded test plant, makes mode switching explicit, and commands the simulated motors directly.

Do not connect this fixture to physical hardware. Real flight software would require verified timing, sensor validation, actuator saturation, watchdogs, estimator integration, formal safety work, and much broader plant testing.
