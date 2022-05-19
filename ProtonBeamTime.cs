using System;
using System.Windows;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VMS.TPS.Common.Model.API;

namespace TreatmentTime
{
    class ProtonBeamTime
    {
        public ProtonBeamTime(IonBeam beamObj)
        {
            EsapiBeamObj = beamObj;
            var result = calcBeamTreatmentTime(beamObj);
        }

        public List<List<double>> BeamOnLayerTimeList { get; set; } = new List<List<double>>();
        public List<List<double>> BeamOffLayerTimeList { get; set; } = new List<List<double>>();
        public List<List<double>> BeamTotLayerTimeList { get; set; } = new List<List<double>>();
        public List<double> TotalBeamTimeList { get; set; } = new List<double>();
        public IonBeam EsapiBeamObj { get; set; }
        public void CalcTotalBeamTime()
        {
            List<double> tempTotalBeamTimeList = new List<double>();
            double layerStartTime = 0;
            for (int i = 0; i < BeamOnLayerTimeList.Count; i++)
            {
                var sumList = BeamOnLayerTimeList[i].Zip(BeamOffLayerTimeList[i], (x, y) => x + y).ToList();
                BeamTotLayerTimeList.Add(sumList);
                double t_end = sumList.Sum() + layerStartTime;
                //double t_end = BeamOffLayerTimeList[i].Sum() + BeamOnLayerTimeList[i].Sum() + layerStartTime;
                //double t_start = t_end - beamOnLayerTimeList[i];
                layerStartTime = t_end + ProBeamLUT.layerSwitchTime;
                tempTotalBeamTimeList.Add(t_end);
            }
            TotalBeamTimeList = tempTotalBeamTimeList;
        }

        private int calcBeamTreatmentTime(IonBeam beam)
        {
            if (!beam.GetEditableParameters().IonControlPointPairs.Any())
            {
                MessageBox.Show("No ControlPoint Pairs in field " + beam.Id.ToString() + ". Might need to recalculate dose.");
                return 0;
            }
            List<double> beamCurrentEstimateList = new List<double>();
            var MUforField = beam.Meterset.Value;
            IonControlPointPairCollection IonControlPointPairList = beam.GetEditableParameters().IonControlPointPairs;
            var cumMetersetWeight = IonControlPointPairList.LastOrDefault().EndControlPoint.MetersetWeight;
            var MUperCumMeterSetWeightForCurrentField = (MUforField / cumMetersetWeight);
            var minMUForFinalLayer = GetMUForControlPointPair(MUperCumMeterSetWeightForCurrentField, IonControlPointPairList.LastOrDefault()).Min();
            var energyForFinalLayer = IonControlPointPairList.LastOrDefault().NominalBeamEnergy;
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
                BeamOffLayerTimeList.Add(GetWaitTime(xPlan, yPlan, CPPair.NominalBeamEnergy));
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
                    estimatedMUperMS[i] = estimatedMUperMS[i] * ProBeamLUT.beamCurrentViolationMUMultiplier;
                }
            }
            for (int i = 0; i < muPlanList.Count; i++)
            {
                BeamOnLayerTimeList.Add(muPlanList[i].Select((dValue, index) => (dValue / estimatedMUperMS[i]) / 1000).ToList());
            }
            CalcTotalBeamTime();
            return 1;
        }

        private List<double> GetBeamCurrentMaxAllowedLayer(List<double> genericNormalizedBeamCurrentList)
        {
            
            List<double> beamCurrentMaxAllowedLayerList = new List<double>(genericNormalizedBeamCurrentList.Count);
            for (int i = 0; i < genericNormalizedBeamCurrentList.Count; i++)
            {
                beamCurrentMaxAllowedLayerList.Add(genericNormalizedBeamCurrentList[i] * ProBeamLUT.beamCurrentMaxAllowedAtMaxEnergy);
                if (beamCurrentMaxAllowedLayerList[i] > ProBeamLUT.beamCurrentMaxAllowedGlobal)
                {
                    beamCurrentMaxAllowedLayerList[i] = ProBeamLUT.beamCurrentMaxAllowedGlobal;
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
                    estimatedBeamCurrent[i] = estimatedBeamCurrent[i] * ProBeamLUT.maxCurrentViolationMultiplier;
                }
                if (estimatedBeamCurrent[i] > ProBeamLUT.beamCurrentMaxAllowedGlobal)
                {
                    estimatedBeamCurrent[i] = beamCurrentMaxAllowedLayer[i];
                }
            }
            for (int i = 0; i < estimatedBeamCurrent.Count; i++)
            {
                double maxDeviationFromMinBeamCurrent = ProBeamLUT.maxBeamCurrentDeviation * estimatedBeamCurrent.Min();
                if (estimatedBeamCurrent[i] > maxDeviationFromMinBeamCurrent)
                {
                    estimatedBeamCurrent[i] = maxDeviationFromMinBeamCurrent;
                }
            }
            return estimatedBeamCurrent;
        }

        private static List<double> GetMUForControlPointPair(double MUperCumMeterSetWeight, IonControlPointPair CPPair)
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
                //System.Windows.MessageBox.Show("Warning: energy of " + energy + " exceeds calculation model bounds. Setting value to " + ProBeamLUT.energyTable.Min() + ". This will increase prediction error.");
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

        private List<double> StepTimeFit(List<double> shiftList, List<double> fit, double energy)
        {
            List<double> stepTimeFit = new List<double>();
            foreach (double shift in shiftList)
            {
                stepTimeFit.Add(fit[0] + fit[1] * shift + fit[2] * energy + fit[3] * shift * energy + fit[4] * energy * energy);
            }
            return stepTimeFit;
        }

        private List<double> GetMaxValues(List<double> xShift, List<double> yShift, List<double> xStepTime, List<double> yStepTime)
        {
            List<double> maxValueXY = new List<double>();
            for (int i = 0; i < xStepTime.Count; i++)
            {
                if (xShift[i] > 10 || yShift[i] > 10)
                {
                    if (xStepTime[i] > yStepTime[i])
                        maxValueXY.Add(xStepTime[i]);
                    else
                    {
                        maxValueXY.Add(yStepTime[i]);
                    }
                }
                else
                {
                    maxValueXY.Add(0.0);
                }
            }
            return maxValueXY;
        }

        private List<double> GetWaitTime(List<double> xLayer, List<double> yLayer, double energy)
        {
            List<double> xShift = GetShift(xLayer);
            List<double> yShift = GetShift(yLayer);

            List<double> stepTimeXfit = StepTimeFit(xShift, ProBeamLUT.xFit, energy);
            List<double> stepTimeYfit = StepTimeFit(yShift, ProBeamLUT.yFit, energy);

            List<double> maxValueXY = GetMaxValues(xShift, yShift, stepTimeXfit, stepTimeYfit);

            return maxValueXY;
        }
    }
}