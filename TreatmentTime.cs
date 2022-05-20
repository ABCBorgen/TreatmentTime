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
    public class Script// : ScriptBase
    {
        public Script()
        {

        }

        const int dec = 4;

        //public override void Run(PluginScriptContext context)
        public void Execute(ScriptContext context)
        {
            PlanSetup plan = context.PlanSetup;
            CalculateTreatmentTime(plan);
        }

        private List<ProtonBeamTime> ProtonBeamTimes = new List<ProtonBeamTime>();

        private void CalculateTreatmentTime(PlanSetup plan)
        {
            IonPlanSetup ionPlan = plan as IonPlanSetup;
            if (ionPlan == null)
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

            List<double> waitTimeLayerList = new List<double>();
            List<double> onTimeLayerList = new List<double>();
            List<ProtonBeamTime> ProtonBeamList = new List<ProtonBeamTime>();
            foreach (IonBeam beam in ionPlan.IonBeams)
            {
                var protonBeam = new ProtonBeamTime(beam);
                ProtonBeamList.Add(protonBeam);
                message += "\r\n" + beam.Id + "\t\t" + Math.Round(protonBeam.BeamOnLayerTimeList.SelectMany(spotList => spotList).Sum(), dec) + "\t\t" + Math.Round(protonBeam.BeamOffLayerTimeList.SelectMany(spotList => spotList).Sum(), dec) + "\t\t" + Math.Round(protonBeam.TotalBeamTimeList.Last(), dec).ToString();
            }
            ProtonBeamTimes = ProtonBeamList;
            System.Windows.MessageBox.Show(message);
            //Uncomment below for detailed messages
            ShowDetailedLayerMessage();
            //ShowDetailedSpotMessage();
        }

        public void ShowDetailedSpotMessage()
        {
            foreach (ProtonBeamTime protonbeam in ProtonBeamTimes)
            {
                
                for (int i = 0; i < protonbeam.BeamOnLayerTimeList.Count; i++)
                {
                    string detailedBeamMessage = "---------------------------------------------------------\r\n";
                    detailedBeamMessage += (protonbeam.EsapiBeamObj.Id.ToString() + ", layer " + i.ToString() + " spot times in seconds. Layer Switch time = 1\r\n");
                    detailedBeamMessage += ("SpotNo\t\tBeamOn\t\tBeamOff\t\tTotal\r\n");
                    for (int j = 0; j < protonbeam.BeamOnLayerTimeList[i].Count; j++)
                    {
                        detailedBeamMessage += (j + 1).ToString() + "\t\t"
                                        + Math.Round(protonbeam.CumBeamOnLayerTimeList[i][j], dec).ToString() + "\t\t"
                                        + Math.Round(protonbeam.CumBeamOffLayerTimeList[i][j], dec).ToString() + "\t\t"
                                        + Math.Round(protonbeam.CumBeamTotLayerTimeList[i][j], dec).ToString() + "\r\n";
                    }
                    System.Windows.MessageBox.Show(detailedBeamMessage);
                }
                
            }
        }

        public void ShowDetailedLayerMessage()
        {
            foreach (ProtonBeamTime protonbeam in ProtonBeamTimes)
            {
                string detailedBeamMessage = "---------------------------------------------------------\r\n";
                detailedBeamMessage += (protonbeam.EsapiBeamObj.Id.ToString() + " layer times in seconds. Layer Switch time = 1\r\n");
                detailedBeamMessage += ("LayerNo\t\tBeamOn\t\tBeamOff\t\tTotal\r\n");
                for (int i = 0; i < protonbeam.BeamOnLayerTimeList.Count; i++)
                {
                    detailedBeamMessage += (i + 1).ToString() + "\t\t"
                                        + Math.Round(protonbeam.BeamOnLayerTimeList[i].Sum(), dec).ToString() + "\t\t"
                                        + Math.Round(protonbeam.BeamOffLayerTimeList[i].Sum(), dec).ToString() + "\t\t"
                                        + Math.Round(protonbeam.TotalBeamTimeList[i], dec).ToString() + "\r\n";
                }
                System.Windows.MessageBox.Show(detailedBeamMessage);
            }
        }

        //Unused function. Setting up for saving beam data.
        private void button1_Click(object sender, System.EventArgs e)
        {
            System.IO.Stream myStream;
            var dlg = new Microsoft.Win32.SaveFileDialog();

            dlg.Filter = "txt files (*.txt)|*.txt|All files (*.*)|*.*";
            dlg.FilterIndex = 2;
            dlg.RestoreDirectory = true;

            if (dlg.ShowDialog() != null)
            {
                if ((myStream = dlg.OpenFile()) != null)
                {
                    // Code to write the stream goes here.
                    myStream.Close();
                }
            }
        }

    }
}