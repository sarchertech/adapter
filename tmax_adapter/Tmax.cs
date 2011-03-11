using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO.Ports;
using System.Diagnostics;
using System.Threading;

namespace tmax_adapter
{
    class Tmax
    {
        SerialPort serialPort;

        int[] status_buffer = new int[264];
        int[] time_buffer = new int[264];
        static int number_of_errors = 0;

        public Tmax()
        {
            set_serial_port_com();
        }

        private bool set_serial_port_com()
        {
            string[] ports = SerialPort.GetPortNames();

            foreach (string com in ports)
            {
                LogEvent("Trying COM " + com, EventLogEntryType.Information);
                try
                {
                    serialPort = new SerialPort(com, 9600, Parity.None, 8, StopBits.One);
                    serialPort.Handshake = Handshake.None;
                    serialPort.WriteTimeout = 500;
                    serialPort.ReadTimeout = 500;
                    serialPort.Open();

                    if (activate(64, 0, 0))
                    {
                        LogEvent("Tmax Com Port set to " + com, EventLogEntryType.Information);
                        break;
                    }
                    else
                    {
                        LogEvent("Tmax Com Port not seet to " + com, EventLogEntryType.Warning);
                        serialPort = null;
                    }
                }
                catch(Exception e)
                {
                    LogEvent("SETCOM ERROR " + e, EventLogEntryType.Error);
                    serialPort = null;
                }
            }
            Thread.Sleep(2000);

            if (serialPort != null)
            {
                return true;
            }
            else
            {
                LogEvent("Unable to Find Tmax Manager COM port. Make sure it's plugged in.", EventLogEntryType.Error);
                return false;
            }
        }

        private void check_serial_port_open()
        {
            try
            {
                if (!(serialPort.IsOpen))
                {
                    serialPort.Open();
                }
            }
            catch
            {
                //set_serial_port_com();
            }
        }

        private static byte check_sum(byte[] array)
        {
            byte sum = 0;

            foreach (byte b in array)
            {
                sum += b;
            }

            return (byte)(sum & 255);
        }

        public bool activate(byte bed, byte duration, byte delay)
        {
            try
            {
                byte[] byte_array = new byte[8] { 200, 30, bed, duration, 1, delay, 0, 0 };

                byte_array[7] = check_sum(byte_array);

                //if (!(serialPort.IsOpen))
                //serialPort.Open();
                check_serial_port_open();

                serialPort.Write(byte_array, 0, 8);

                int[] abc = new int[3];
                abc[0] = serialPort.ReadByte();
                abc[1] = serialPort.ReadByte();
                abc[2] = serialPort.ReadByte();

                bool retval = false;

                if (abc[0] == 6 && abc[1] == 0 && abc[2] == 6)
                {
                    //Console.WriteLine("activate true");
                    number_of_errors = 0;
                    retval = true;
                }
                else
                {
                    //Console.WriteLine("activate false");
                    LogEvent("(Return value from tmax wrong) Failed to activate bed " + bed.ToString(), EventLogEntryType.Warning);
                    clear_cache();
                }
                return retval;
            }
            catch(Exception e)
            {
                LogEvent("(No return value from tmax) Failed to activate bed " + bed.ToString() + ". " + e, EventLogEntryType.Warning);
                clear_cache();
                return false;
            }
        }

        public void status()
        {
            try
            {
                byte[] byte_array = new byte[8] { 42, 0, 0, 132, 177, 0, 119, 188 };

                //if (!(serialPort.IsOpen))
                //serialPort.Open();
                check_serial_port_open();

                serialPort.Write(byte_array, 0, 8);

                for (int i = 0; i < 264; i++)//TODO change to for loop
                {
                    status_buffer[i] = serialPort.ReadByte();
                }
            }
            catch(Exception e)
            {
                LogEvent("Failed to check status. " + e, EventLogEntryType.Warning);
                clear_cache();
            }
        }

        public void time()
        {
            try
            {
                byte[] byte_array = new byte[8] { 42, 0, 0, 141, 181, 0, 37, 233 };

                //if (!(serialPort.IsOpen))
                //serialPort.Open();
                check_serial_port_open();

                serialPort.Write(byte_array, 0, 8);

                for (int i = 0; i < 264; i++)
                {
                    time_buffer[i] = serialPort.ReadByte(); ;
                }

                if ((time_buffer[0] == 42 && time_buffer[3] == 141 && time_buffer[4] == 181))
                {
                    //Console.Write("time true");
                    number_of_errors = 0;
                }
                else
                {
                    LogEvent("(Return value from tmax wrong) Failed to check time", EventLogEntryType.Warning);
                    clear_cache();
                    if (number_of_errors > 30)
                    {
                        set_serial_port_com();
                        Thread.Sleep(4000);
                    }
                }

                //Console.WriteLine("\n------");
            }
            catch(Exception e)
            {
                LogEvent("Failed to check time. " + e, EventLogEntryType.Warning);
                clear_cache();
                if (number_of_errors > 30)
                {
                    set_serial_port_com();
                    Thread.Sleep(4000);
                }
            }
        }

        private void clear_cache()
        {
            // check serial port if not open try to open if still not open try to find it
            LogEvent("clear_cache was called", EventLogEntryType.Warning);
            //check_serial_port_open();

            try
            {
                while (true)
                {
                    int a = serialPort.ReadByte();
                }
            }
            catch
            {

            }

            if (number_of_errors > 25)
            {
                //Thread.Sleep(250);
                Thread.Sleep(2000);
            }
            else
            {
                Thread.Sleep(250);
            }
        }

        public int[] status_buffer_accessor()
        {
            return status_buffer;
        }

        public int[] time_buffer_accessor()
        {
            return time_buffer;
        }

        public void close_serial_port()
        {
            if (serialPort.IsOpen)
            {
                serialPort.Close();
            }
        }

        static void LogEvent(String Message, EventLogEntryType type)
        {
            if (number_of_errors <= 25)
            {
                number_of_errors++;
                String source = "TMAX_CLASS";
                String log = "Application";
                if (!EventLog.SourceExists(source))
                {
                    EventLog.CreateEventSource(source, log);
                }

                EventLog eLog = new EventLog();
                eLog.Source = source;

                eLog.WriteEntry(Message, type, number_of_errors);
            }
            else if (number_of_errors < 2000000)
            {
                number_of_errors++;
            }
        }
    }
}
