using S7.Net;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;

namespace EngineNumber_checker
{
    public partial class Main : Form
    {
        Logger logger = new Logger();
        Process_handler process_Handler = new Process_handler();

        SqlConnection cnn;
        SqlCommand command;
        SqlDataReader reader;

        string connetionString;
        string timeOffSetForSQL = "3";
        public string CurrentEngine = string.Empty;
        public string queryResultQualityData = string.Empty;
        public string queryResultREG_TM = string.Empty;
        public string queryResultREG_DT = string.Empty;

        int engineLifeTime = 3;

        bool startBtn = false;

        Plc PLC_M1 = new Plc(CpuType.S7300, "140.100.101.1", 0, 2);

        DuplicateDetected duplicateDetectedForm;
        IntervalForm form2;

        public Main()
        {
            duplicateDetectedForm = new DuplicateDetected(this, process_Handler, logger);
            form2 = new IntervalForm(this);

            InitializeComponent();

            btn_Start.PerformClick();                       //This causes the application to open
            btn_Start_Click(new object(), new EventArgs()); //and start running immediately.

            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.FormIsClosing);
        }

        private void FormIsClosing(object sender, FormClosingEventArgs e) //This is called when app is closed
        {
            logger.Log("EngineNumber-Checker app Closed!");
        }

        public void EngineDuplicateDetected()
        {
            duplicateDetectedForm.RefreshData();
            duplicateDetectedForm.Show();
        }

        public string PLC_GetBlockOrHead()
        {
            PLC_M1.Open();
            var bytes = PLC_M1.ReadBytes(DataType.DataBlock, 180, 1, 1);
            string message = (Convert.ToChar(bytes[0]).ToString());
            PLC_M1.Close();

            return message;
        }

        public string GetCurrentEngineBySQL()
        {
            string output = string.Empty;

            string query = " SELECT TOP (1) " +
                          " [ENG_SN]" +
                          " FROM [HMB].[SQS].[ENG_BUILD_DATA]" + " WHERE " +
                          " ( REG_TM > REPLACE(convert(time(0), getdate()), ':', '') - " + form2.getEngineLifeTimeMsValue +
                          " AND " + "REG_TM < REPLACE(convert(time(0), getdate()), ':', '') )" + " AND " +
                          "( BLK_NO = ' ' AND REG_DT = REPLACE(convert(date, getdate()), '-', '') )" +
                          " ORDER by ID desc ";

            connetionString = @"Data Source=localhost;Initial Catalog=HMB;User ID=sa;Password=T00lsNetPwd;Trusted_Connection=true";
            //connetionString = @"Data Source=" + form2.getConnectionString + ";Initial Catalog=HMB;User ID=EngineNumber-APP;Password=sqs";

            cnn = new SqlConnection(connetionString);
            cnn.Open();

            command = new SqlCommand(query, cnn);
            reader = command.ExecuteReader();

            if (reader.Read())
                output = reader.GetString(0);

            reader.Close();
            command.Dispose();
            cnn.Close();

            return output;
        }

        public void EngineValidate()
        {
            CurrentEngine = GetCurrentEngineBySQL();

            if (CurrentEngine != string.Empty)
            {
                tb_Console.Text = "New engine detected: " + CurrentEngine + "\r\n" + tb_Console.Text;
                logger.Log("New engine detected: " + CurrentEngine);

                if (PLC_GetBlockOrHead() == "B")
                {
                    logger.Log("\r\nIt is a BLOCK" + "\r\n");

                    string query = "SELECT TOP (1)" +
                    "[ENG_NO]" +
                    ",[QUALITY_DATA]" +
                    ",[REG_DT]" +
                    ",[REG_TM]" +
                    "FROM [HMB].[MES].[Q_QUALITY_SEND_IF]" +
                    "WHERE " +
                    "ENG_NO = " + "'" + CurrentEngine + "'" +
                    "AND (QM_CD = 'BKA00-100-01-M1' AND QUALITY_DATA LIKE '%OK        Barcode   OK        Block     B%')";

                    connetionString = @"Data Source=localhost;Initial Catalog=HMB;User ID=sa;Password=T00lsNetPwd;Trusted_Connection=true";
                    //connetionString = @"Data Source=" + form2.getConnectionString + ";Initial Catalog=HMB;User ID=EngineNumber-APP;Password=sqs";
                    cnn = new SqlConnection(connetionString);
                    cnn.Open();

                    command = new SqlCommand(query, cnn);
                    reader = command.ExecuteReader();

                    object[] results = new object[4];
                    while (reader.Read())
                    {
                        reader.GetValues(results);
                    }

                    if (results[0] != null)
                    {
                        string queryResultEngine = results[0].ToString();
                        queryResultQualityData = results[1].ToString().Substring(40, 13);
                        queryResultREG_DT = results[2].ToString();
                        queryResultREG_TM = results[3].ToString();

                        if (queryResultEngine != null)
                        {
                            Timer2.Stop();

                            queryResultREG_DT = queryResultREG_DT.Insert(4, "/");
                            queryResultREG_DT = queryResultREG_DT.Insert(7, "/");

                            queryResultREG_TM = queryResultREG_TM.Insert(2, ":");
                            queryResultREG_TM = queryResultREG_TM.Insert(5, ":");


                            logger.Log("REPEATED ENGINE DETECTED: " + CurrentEngine + "\r\n" +
                                "Block linked: " + queryResultQualityData + "\r\n" + "Date: " + queryResultREG_DT +
                                " - " + queryResultREG_TM);

                            tb_Console.Text = "=========\r\nREPEATED ENGINE DETECTED: " + CurrentEngine + "\r\n" +
                                "Block linked: " + queryResultQualityData + "\r\n=========\r\n" + "Date: " + queryResultREG_DT +
                                " - " + queryResultREG_TM + tb_Console.Text;

                            tb_Console.Text = "Process Suspended" + tb_Console.Text;
                            //process_Handler.SuspendProcess(GetProcessID("PlcStationClient"));
                            logger.Log("PLC process suspended!");

                            EngineDuplicateDetected();
                        }

                    }
                    else
                    {
                        tb_Console.Text = "=====\r\nResult: OK.\r\n=====" + tb_Console.Text;
                        logger.Log("Result: OK.");
                    }

                    reader.Close();
                    command.Dispose();
                    cnn.Close();
                }
                else
                {
                    tb_Console.Text = "\r\nit was a HEAD - Validation disregarded\r\n" + tb_Console.Text;
                    logger.Log("it is a HEAD - The EngineNumber validation will be disregarded");
                }
            }
            else
            {
                tb_Console.Text = "SQL: No recent EngineNumber available \r\n" + tb_Console.Text;
            }
        }

        private void btn_Start_Click(object sender, EventArgs e)
        {
            if (startBtn)
            {
                if (tb_Console.Text == null)
                    pn_AtlasLogo.Show();

                btn_Start.BackColor = Color.Green;
                btn_Start.Text = "Start";
                startBtn = false;
                lb_Timer_Tick.BackColor = Color.LightSlateGray;
                pn_StopRunning.BackColor = Color.LightSlateGray;
                lb_Timer_Tick.Text = "Stopped";

                Timer2.Stop();
                logger.Log("EngineNumber-Checker Stopped!");
            }
            else
            {
                logger.Log("EngineNumber-Checker Started!");

                pn_AtlasLogo.Hide();

                Timer2_Tick(sender, e);

                btn_Start.BackColor = Color.Crimson;
                btn_Start.Text = "Stop";
                startBtn = true;
                lb_Timer_Tick.Text = "Running";

                Timer2.Start();

            }


        }

        private void Timer2_Tick(object sender, EventArgs e)
        {
            if (tb_Console.Text.Length > 300)
            {
                tb_Console.Text = string.Empty;
                logger.Log("SQL: No recent EngineNumber available TICK");
            }

            EngineValidate();

            if (lb_Timer_Tick.BackColor == Color.LightSlateGray)
            {
                lb_Timer_Tick.BackColor = Color.Gold;
                pn_StopRunning.BackColor = Color.Gold;
            }
            else
            {
                lb_Timer_Tick.BackColor = Color.LightSlateGray;
                pn_StopRunning.BackColor = Color.LightSlateGray;
            }
        }

        public void SaveParameters()
        {
            Timer2.Interval = form2.getTimerTickMsValue;
            engineLifeTime = form2.getEngineLifeTimeMsValueInt;
            tb_Console.Text = "\r\nPARAMETERS SAVED\r\n" + tb_Console.Text;
        }

        private void btn_Options_Click(object sender, EventArgs e)
        {
            form2.Show();
        }
    }
}