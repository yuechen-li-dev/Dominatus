# Dominatus 1.0 package manifest

The 1.0 release workflow packs and may publish only these packages, in this order: `Dominatus.Core`, `Dominatus.OptFlow`, `Dominatus.UtilityLite`, `Ariadne.OptFlow`, `Dominatus.Actuators.Standard`, `Dominatus.Actuators.HomeAssistant`, `Dominatus.Actuators.Audio`, `Dominatus.Actuators.Payments`, `Dominatus.Actuators.Payments.Stripe`, and `Dominatus.Actuators.Payments.PayPal`. Each targets `1.0.0`; project references bind to the packed 1.0 graph.

`UtilityLite` remains a compatibility package and depends on OptFlow. Ariadne depends on Core and OptFlow. Standard, Home Assistant, Audio, and Payments depend on Core; provider packages depend on Payments. The generator is shipped inside OptFlow as an analyzer, never as a runtime library.

The LLM, server, asset, sprite, and engine-connector packages remain preview/experimental and are intentionally excluded. Samples, tests, and generators are not publishable release packages.
