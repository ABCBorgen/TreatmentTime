# TreatmentTime
Calculates the time it takes to deliver fields from a proton plan. WIP. Each center must perform their own data collection and quality assurance before employing the program.


TreatmentTime.esapi.dll can be run directly from Eclipse to get a list for delivery duration for each field and layer in an open plan.

Include ProtonBeamTime.cs and ProBeamLUT.cs in your project to perform calculations. ProBeamLUT.cs contains site-specific parameters.

Quick example:

```cs
    using TreatmentTime
    //...
    var pbt = new ProtonBeamTime(beam);
    pbt.BeamOnLayerTimeList  // List of lists of spot on times for each layer
    pbt.BeamOffLayerTimeList // List of lists of spot off times for each layer
    pbt.BeamTotLayerTimeList // List of lists of spot on + spot off for each layer
    pbt.CumBeamTotLayerTimeList // List of lists of spot on + spot off for each layer, added cumulatively, including layer switch times.
    pbt.TotalBeamTimeList    // List of delivery times for each field. This includes layer switch times.`
```
