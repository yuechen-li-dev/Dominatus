# OPTFLOW-1.0-AUDIT-A1: quadcopter control fixture

This incremental fixture substitutes a direct quadcopter roll controller for the proposed mobile-robot example. Dominatus itself closes the simulated feedback loop: it observes roll state, selects an explicit correction mode, and emits normalized motor-mix commands. A deterministic in-process plant supplies the next observation; no PID or nonlinear controller sits below the authored flow.

The current fixture is intentionally one-axis and simulation-only. It excludes hardware, ROS, external simulators, device drivers, and real-time claims. A later audit increment should add disturbances, actuator saturation, trace assertions, and deferred-operation checkpoint/replay before making broader robotics or authoring claims.
