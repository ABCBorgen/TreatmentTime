using System;
using System.Linq;
using System.Text;
using System.Windows;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;
using EsapiEssentials.Plugin;
using TreatmentTime;

// TODO: Replace the following version attributes by creating AssemblyInfo.cs. You can do this in the properties of the Visual Studio project.
[assembly: AssemblyVersion("1.0.0.1")]
[assembly: AssemblyFileVersion("1.0.0.1")]
[assembly: AssemblyInformationalVersion("1.0")]

// TODO: Uncomment the following line if the script requires write access.
// [assembly: ESAPIScript(IsWriteable = true)]

namespace VMS.TPS
{
    public class Script : ScriptBase
    {
        public Script()
        {

        }

        const int dec = 4;
        //public void Execute(ScriptContext context)
        public override void Run(PluginScriptContext context)
        {
            PlanSetup plan = context.PlanSetup;
            CalculateTreatmentTime(plan);
        }

        private List<ProtonBeam> ProtonBeams = new List<ProtonBeam>();

        private void CalculateTreatmentTime(PlanSetup plan)
        { 
            //PlanSetup plan = context.PlanSetup;
            IonPlanSetup ionPlan = plan as IonPlanSetup;
            if(ionPlan == null)
            {
                MessageBox.Show("Please load a plan.");
                return;
            }
            if (!ionPlan.IonBeams.Any())
            {
                MessageBox.Show("No fields in plan.");
                return;
            }
            string message = "Field delivery times in seconds. Layer Switch time = 1\r\n";
            message += "Field\t\tOn Time\t\tWait Time\t\tTotal Time";
            //message += "---------------------------------------------------------\r\n";
            
            double waitTime = 0.0;
            List<double> waitTimeLayerList = new List<double>();
            List<double> onTimeLayerList = new List<double>();
            List<ProtonBeam> ProtonBeamList = new List<ProtonBeam>();
            double cumMetersetWeight = 0.0;
            double MUforField = 0.0;
            double MUperCumMeterSetWeightForCurrentField = 0.0;
            double minMUForFinalLayer = 0.0;
            double energyForFinalLayer = 0.0;
            

            foreach (IonBeam beam in ionPlan.IonBeams)
            {
                if(!beam.GetEditableParameters().IonControlPointPairs.Any())
                {
                    MessageBox.Show("No ControlPoint Pairs in field " + beam.Id.ToString() + ". Might need to recalculate dose.");
                }
                ProtonBeam protonbeam = new ProtonBeam(beam);
                List<double> beamCurrentEstimateList = new List<double>();
                MUforField = beam.Meterset.Value;
                IonControlPointPairCollection IonControlPointPairList = beam.GetEditableParameters().IonControlPointPairs;
                cumMetersetWeight = IonControlPointPairList.LastOrDefault().EndControlPoint.MetersetWeight;
                MUperCumMeterSetWeightForCurrentField = (MUforField / cumMetersetWeight);
                minMUForFinalLayer = GetMUForControlPointPair(MUperCumMeterSetWeightForCurrentField, IonControlPointPairList.LastOrDefault()).Min();
                energyForFinalLayer = IonControlPointPairList.LastOrDefault().NominalBeamEnergy;
                List<List<double>> muPlanList = new List<List<double>>();
                List<double> CPPairEnergyList = new List<double>();
                List<double> genericNormalizedBeamCurrentList = new List<double>();
                foreach (IonControlPointPair CPPair in IonControlPointPairList)
                {
                    List<double> xPlan = new List<double>();
                    List<double> yPlan = new List<double>();
                    List<double> spotWeights = new List<double>();
                    foreach (IonSpotParameters spot in CPPair.FinalSpotList)
                    {
                        xPlan.Add(spot.X);
                        yPlan.Add(spot.Y);
                    }
                    List<double> muPlan = GetMUForControlPointPair(MUperCumMeterSetWeightForCurrentField, CPPair);
                    muPlanList.Add(muPlan);
                    CPPairEnergyList.Add(CPPair.NominalBeamEnergy);
                    double genericNormalizedBeamCurrent = LinearInterp(CPPair.NominalBeamEnergy, ProBeamLUT.energyTable, ProBeamLUT.genericNormalizedBeamCurrentTable);
                    genericNormalizedBeamCurrentList.Add(genericNormalizedBeamCurrent);
                    beamCurrentEstimateList.Add(GetBeamCurrentEstimate(muPlan, genericNormalizedBeamCurrent, minMUForFinalLayer));
                    waitTime = GetWaitTime(xPlan, yPlan, CPPair.NominalBeamEnergy);
                    protonbeam.BeamOffLayerTimeList.Add(waitTime);
                }

                List<double> beamCurrentReEstimateList = GetBeamCurrentReEstimateList(beamCurrentEstimateList, muPlanList, CPPairEnergyList);
            
                List<double> beamCurrentMaxAllowedLayerList = GetBeamCurrentMaxAllowedLayer(genericNormalizedBeamCurrentList);
                List<bool> beamCurrentViolationFlags = GetBeamCurrentViolations(beamCurrentReEstimateList, beamCurrentMaxAllowedLayerList);
                List<double> beamCurrentNoViolations = RemoveBeamCurrentViolations(beamCurrentReEstimateList, beamCurrentMaxAllowedLayerList, beamCurrentViolationFlags);

                List<double> estimatedMUperMS = new List<double>();
                for (int i = 0; i < beamCurrentNoViolations.Count; i++)
                {
                    estimatedMUperMS.Add(GetMUperMS(CPPairEnergyList[i], beamCurrentNoViolations[i]));
                    if (beamCurrentViolationFlags[0] && !beamCurrentViolationFlags[i])
                    {
                        estimatedMUperMS[i] = estimatedMUperMS[i] * 0.9;
                    }
                }
                for (int i = 0; i < muPlanList.Count; i++)
                {
                    protonbeam.BeamOnLayerTimeList.Add((muPlanList[i].Select((dValue, index) => (dValue / estimatedMUperMS[i]) / 1000)).Sum());
                }
                protonbeam.CalcTotalBeamTime();
                ProtonBeamList.Add(protonbeam);
                message += "\r\n" + beam.Id + "\t\t" + Math.Round(protonbeam.BeamOnLayerTimeList.Sum(), dec) + "\t\t" + Math.Round(protonbeam.BeamOffLayerTimeList.Sum(), dec) + "\t\t" + Math.Round(protonbeam.TotalBeamTimeList.Last(), dec).ToString();
            }
            ProtonBeams = ProtonBeamList;
            System.Windows.MessageBox.Show(message);
            ShowDetailedBeamMessage();
        }

        

        public void ShowDetailedBeamMessage()
        {
            foreach (ProtonBeam protonbeam in ProtonBeams)
            {
                string detailedBeamMessage = "---------------------------------------------------------\r\n";
                detailedBeamMessage += (protonbeam.EsapiBeamObj.Id.ToString() + " layer times in seconds. Layer Switch time = 1\r\n");
                detailedBeamMessage += ("LayerNo\t\tBeamOn\t\tBeamOff\t\tTotal\r\n");
                for (int i = 0; i < protonbeam.BeamOnLayerTimeList.Count; i++)
                {
                    detailedBeamMessage += (i + 1).ToString() + "\t\t"
                                        + Math.Round(protonbeam.BeamOnLayerTimeList[i], dec).ToString() + "\t\t"
                                        + Math.Round(protonbeam.BeamOffLayerTimeList[i], dec).ToString() + "\t\t"
                                        + Math.Round(protonbeam.TotalBeamTimeList[i], dec).ToString() + "\r\n";
                }
                System.Windows.MessageBox.Show(detailedBeamMessage);
            }
        }

        private void calcBeamCurrentAndWaitTimeForCPPair()
        {

        }

        private List<double> GetBeamCurrentMaxAllowedLayer(List<double> genericNormalizedBeamCurrentList)
        {
            const int beamCurrentMaxAllowedGlobal = 550;
            const double beamCurrentMaxAllowedAtMaxEnergy = 190.3826;
            List<double> beamCurrentMaxAllowedLayerList = new List<double>(genericNormalizedBeamCurrentList.Count);
            for (int i = 0; i < genericNormalizedBeamCurrentList.Count; i++)
            {
                beamCurrentMaxAllowedLayerList.Add(genericNormalizedBeamCurrentList[i] * beamCurrentMaxAllowedAtMaxEnergy);
                if (beamCurrentMaxAllowedLayerList[i] > beamCurrentMaxAllowedGlobal)
                {
                    beamCurrentMaxAllowedLayerList[i] = beamCurrentMaxAllowedGlobal;
                }
            }
            return beamCurrentMaxAllowedLayerList;
        }

        private List<bool> GetBeamCurrentViolations(List<double> beamCurrentList, List<double> beamCurrentMaxAllowedLayerList)
        {
            List<bool> isMaxCurrentViolated = new List<bool>();
            for (int i = 0; i < beamCurrentList.Count; i++)
            {
                if (beamCurrentList[i] > beamCurrentMaxAllowedLayerList[i])
                {
                    isMaxCurrentViolated.Add(true);
                }
                else
                {
                    isMaxCurrentViolated.Add(false);
                }
            }
            return isMaxCurrentViolated;
        }

        private List<double> RemoveBeamCurrentViolations(List<double> estimatedBeamCurrent, List<double> beamCurrentMaxAllowedLayer, List<bool> isMaxCurrentViolated)
        {
            for (int i = 0; i < estimatedBeamCurrent.Count; i++)
            {
                if (isMaxCurrentViolated[0] && !isMaxCurrentViolated[i])
                {
                    estimatedBeamCurrent[i] = estimatedBeamCurrent[i] * 1.05;
                }
                if (estimatedBeamCurrent[i] > 550)
                {
                    estimatedBeamCurrent[i] = beamCurrentMaxAllowedLayer[i];
                }
            }
            for (int i = 0; i < estimatedBeamCurrent.Count; i++)
            {
                double maxDeviationFromMinBeamCurrent = 100 * 7 * estimatedBeamCurrent.Min();
                if (estimatedBeamCurrent[i] > maxDeviationFromMinBeamCurrent)
                {
                    estimatedBeamCurrent[i] = maxDeviationFromMinBeamCurrent;
                }
            }
            return estimatedBeamCurrent;
        }

        private List<double> GetMUForControlPointPair(double MUperCumMeterSetWeight, IonControlPointPair CPPair)
        {
            List<double> spotWeights = new List<double>();
            foreach (IonSpotParameters spot in CPPair.FinalSpotList)
            {
                spotWeights.Add(spot.Weight);
            }
            List<double> muPlan = spotWeights.Select((dValue, index) => dValue * MUperCumMeterSetWeight).ToList();
            return muPlan;
        }

        private List<double> GetShift(List<double> posMap)
        {
            List<double> mapShift = new List<double>();
            mapShift.Add(0.0);
            mapShift.AddRange(posMap.GetRange(0, posMap.Count() - 1).Select((dValue, index) => Math.Abs(posMap[index + 1] - dValue)).ToList());
            return mapShift;
        }

        private int GetEnergyIndexFloor(double energy)
        {
            for (int i = 0; i < ProBeamLUT.energyTable.Count; i++)
            {
                if (ProBeamLUT.energyTable[i] - energy > 0)
                    return i - 1;
            }
            return 0;
        }

        private double LinearInterp(double energy, List<double> xList, List<double> yList)
        {
            if (energy > ProBeamLUT.energyTable.Max())
            {
                // System.Windows.MessageBox.Show("Warning: energy of " + energy + " exceeds calculation model bounds. Setting value to " + ProBeamLUT.energyTable.Max() + ". This will increase prediction error.");
                energy = ProBeamLUT.energyTable.Max();
                return yList[ProBeamLUT.energyTable.IndexOf(energy)];
            }
            else if (energy < ProBeamLUT.energyTable.Min())
            {
                System.Windows.MessageBox.Show("Warning: energy of " + energy + " exceeds calculation model bounds. Setting value to " + ProBeamLUT.energyTable.Min() + ". This will increase prediction error.");
                energy = ProBeamLUT.energyTable.Min();
                return yList[ProBeamLUT.energyTable.IndexOf(energy)];
            }

            int indexFloor = GetEnergyIndexFloor(energy);
            if ((xList[indexFloor + 1] - xList[indexFloor]) == 0)
            {
                return (yList[indexFloor] + yList[indexFloor + 1]) / 2;
            }
            return yList[indexFloor] + (energy - xList[indexFloor]) * (yList[indexFloor + 1] - yList[indexFloor]) / (xList[indexFloor + 1] - xList[indexFloor]);
        }

        private double GetMUperMS(double energy, double beamCurrent)
        {
            double p1 = LinearInterp(energy, ProBeamLUT.energyTable, ProBeamLUT.p1Table);
            double p2 = LinearInterp(energy, ProBeamLUT.energyTable, ProBeamLUT.p2Table);
            return p1 * beamCurrent + p2;
        }

        private double GetBeamCurrentEstimate(List<double> muPlan, double genericNormalizedBeamCurrent, double finalLayerMU)
        {
            double scaledMinMu = muPlan.Min() / finalLayerMU;
            double scaledBeamCurrent = genericNormalizedBeamCurrent * scaledMinMu;
            return scaledBeamCurrent;
        }

        private List<double> GetBeamCurrentReEstimateList(List<double> scaledBeamCurrentList, List<List<double>> planMUList, List<double> energy)
        {
            const int beamCurrentMaxAllowedGlobal = 550;
            double minAllowedSpotDuration = 0.0030;
            double scaledBeamCurrentMax = scaledBeamCurrentList.Max();
            List<double> beamCurrentReEstimateList = scaledBeamCurrentList.Select((dValue, index) => ((beamCurrentMaxAllowedGlobal / scaledBeamCurrentMax) * dValue)).ToList();
            for (int i = 0; i < 2; i++)
            {
                List<double> estimatedMinSpotDurationList = new List<double>();
                for (int j = 0; j < beamCurrentReEstimateList.Count; j++)
                {
                    double estimatedMUperMStemp = GetMUperMS(energy[j], beamCurrentReEstimateList[j]);
                    estimatedMinSpotDurationList.Add(planMUList[j].Min() / estimatedMUperMStemp / 1000);
                }
                beamCurrentReEstimateList = beamCurrentReEstimateList.Select((dValue, index) => (dValue * estimatedMinSpotDurationList.Min()) / minAllowedSpotDuration).ToList();
            }
            return beamCurrentReEstimateList;
        }

        private double GetWaitTime(List<double> xLayer, List<double> yLayer, double energy)
        {
            List<double> xShift = GetShift(xLayer);
            List<double> yShift = GetShift(yLayer);

            //Create function
            List<double> stepTimeXfit = new List<double>();
            foreach (double shift in xShift)
            {
                stepTimeXfit.Add(ProBeamLUT.xFit[0] + ProBeamLUT.xFit[1] * shift + ProBeamLUT.xFit[2] * energy + ProBeamLUT.xFit[3] * shift * energy + ProBeamLUT.xFit[4] * energy * energy);
            }

            List<double> stepTimeYfit = new List<double>();
            foreach (double shift in yShift)
            {
                stepTimeYfit.Add(ProBeamLUT.yFit[0] + ProBeamLUT.yFit[1] * shift + ProBeamLUT.yFit[2] * energy + ProBeamLUT.yFit[3] * shift * energy + ProBeamLUT.yFit[4] * energy * energy);
            }

            List<double> maxValueXY = new List<double>();
            for (int i = 0; i < stepTimeXfit.Count; i++)
            {
                if (xShift[i] > 10 || yShift[i] > 10)
                {
                    if (stepTimeXfit[i] > stepTimeYfit[i])
                        maxValueXY.Add(stepTimeXfit[i]);
                    else
                    {
                        maxValueXY.Add(stepTimeYfit[i]);
                    }
                }
                else
                {
                    maxValueXY.Add(0.0);
                }
            }
            double timeWaitSum = maxValueXY.Sum();
            return timeWaitSum;
        }
    }
}