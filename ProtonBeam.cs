using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VMS.TPS.Common.Model.API;

namespace TreatmentTime
{
    class ProtonBeam
    {
        const float layerShiftTime = 1;
        public List<List<double>> BeamOnLayerTimeList { get; set; } = new List<List<double>>();
        public List<List<double>> BeamOffLayerTimeList { get; set; } = new List<List<double>>();
        public List<double> TotalBeamTimeList { get; set; } = new List<double>();
        public void CalcTotalBeamTime()
        {
            List<double> tempTotalBeamTimeList = new List<double>();
            double layerStartTime = 0;
            for (int i = 0; i < BeamOnLayerTimeList.Count; i++)
            {
                double t_end = BeamOffLayerTimeList[i].Sum() + BeamOnLayerTimeList[i].Sum() + layerStartTime;
                //double t_start = t_end - beamOnLayerTimeList[i];
                layerStartTime = t_end + layerShiftTime;
                tempTotalBeamTimeList.Add(t_end);
            }
            TotalBeamTimeList = tempTotalBeamTimeList;
        }
        public IonBeam EsapiBeamObj { get; set; }
        
        public ProtonBeam()
        {

        }
        public ProtonBeam(IonBeam beamObj)
        {
            EsapiBeamObj = beamObj;
        }
    }
}