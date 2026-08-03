# Robotics control with Dominatus

Dominatus is a general-purpose hybrid control kernel. It composes continuous numerical control, explicit automata, utility decisions, delayed typed operations, recovery state, and optional higher-level reasoning in one inspectable execution model.

Dominatus does not replace control theory. It gives control theory a coherent software execution model for realistic systems.

The M7 quadcopter demonstration uses this hierarchy:

```text
bounded attitude/rate feedback and delay prediction
    ↓
Dominatus utility arbitration
    ↓
persistent recovery and safety states
    ↓
optional bounded LLM decision for an unclassified anomaly
```

The numerical layer still matters. M7 uses confidence-weighted estimation, proportional attitude correction, angular-rate damping, a four-motor mixer, saturation limiting, known-authority compensation, and a small delay-horizon predictor. Dominatus selects and persists the regime in which those calculations run.

## Why the composition layer matters

Real robots do more than solve one feedback equation. They arm and disarm, synchronize observations with different timestamps, notice stale or contradictory sensors, compensate for degraded actuators, limit commands under saturation, preserve recovery attempts, and coordinate operations that complete later. A mature conventional stack can do all of this well. Its complexity naturally appears in estimator objects, mode flags, callback ordering, watchdogs, publishers, recovery coordinators, and diagnostic mirrors.

Dominatus makes those temporal and policy concerns authored states and typed operations. In the M7 sample, `SensorDegraded`, `SensorConflict`, `ActuatorDegraded`, `SafeHover`, `ControlledDescent`, and `EmergencyStop` are durable identities rather than incidental combinations of flags. Utility scores make safety dominance and tie behavior visible. OpenCV vision and LLM escalation are explicit external obligations.

This is a programming-model claim, not a claim that state machines outperform PID, geometric control, Kalman filters, MPC, Lyapunov analysis, or PX4. Those techniques answer important estimation, control, and assurance questions. “Nonlinear” describes most physical systems but does not by itself specify how estimation, switching, recovery, timing, and mission logic should be composed in software.

## The role of frontier LLMs

Frontier LLMs belong above the fast control loop in most cases. They author, inspect, adapt, and escalate control policies rather than manually issuing every actuator command.

M7 never asks an LLM whether motor 2 should change by three percent on the next 20 ms tick. Known wind, sensor dropout, sensor conflict, actuator loss, delay, and saturation all use authored bounded behavior with zero LLM calls. Only a persistent unclassified anomaly first enters `SafeHover` and may then invoke a fake deterministic LLM to choose among authored strategies. Its output routes through an explicit state transition and can never become a raw motor command.

A useful LLM-authored policy is closer to:

> When IMU confidence falls but vision remains stable, reduce authority, use vision-corrected attitude, limit aggressive motion, and escalate only if the disagreement remains unexplained.

Dominatus executes that policy quickly and transparently. The LLM can remain the most general and slowest decider: control-system author, planner, anomaly interpreter, recovery-strategy selector, test author, and controller reviewer.

## Evidence and limits

The one-axis regression remains as a compact proof. M7 adds a deterministic three-axis attitude plant with unequal inertia, cross-axis coupling, damping, delayed commands, wind torque, motor saturation, sensor noise/dropout/conflict, and front-left authority loss. Both the Dominatus and conventional controllers run the same plant and criteria. See [the M7 comparison](../experiments/DOMINATUS_VS_CONVENTIONAL_ROBOTICS_CONTROL.md) for results and threats to validity.

The demonstration is not a formal stability proof, a hard-real-time benchmark, certified flight software, a complete rigid-body/aerodynamic model, or a comparison against production PX4. Do not connect it to physical hardware.
