using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Net;
using System.IO;

namespace tmax_adapter
{
    partial class ToastyTmaxAdapter : ServiceBase
    {
        static ManualResetEvent threadStop = new ManualResetEvent(true);

        static ManualResetEvent mainThreadStop = new ManualResetEvent(false);

        static Tmax tmax;

        public ToastyTmaxAdapter()
        {
            InitializeComponent();
            LogEvent("Passed InitialzeComponent!", EventLogEntryType.Information);
        }

        protected override void OnStart(string[] args)
        {
            //LogEvent("Before Worker started", EventLogEntryType.Information);
            tmax = new Tmax();
            
            var worker = new Thread(mainWebserver);
            worker.Name = "mainWebserver";
            worker.IsBackground = false;
            worker.Start();
            LogEvent("Main Thread started", EventLogEntryType.Information);
        }

        protected override void OnStop()
        {
            LogEvent("Service Received Stop Signal", EventLogEntryType.Information);
            threadStop.Reset();
            mainThreadStop.Reset();
            Thread.Sleep(1200);
            tmax.close_serial_port();
        }

        static void LogEvent(String Message, EventLogEntryType type)
        {
            String source = "TMAX_ADAPTER";
            String log = "Application";
            if (!EventLog.SourceExists(source))
            {
                EventLog.CreateEventSource(source, log);
            }

            EventLog eLog = new EventLog();
            eLog.Source = source;

            eLog.WriteEntry(Message, type);
        }

        void mainWebserver()
        {            
            //start status checking thread
            ThreadStart tmaxCheckerStarter = delegate { status(); };

            Thread tmaxChecker = new Thread(tmaxCheckerStarter);

            tmaxChecker.Start();

            LogEvent("TmaxChecker thread started", EventLogEntryType.Information);

            //start status check listener
            ThreadStart listnerStatusStarter = delegate { statusListener(); };

            Thread listenerStatusThread = new Thread(listnerStatusStarter);

            listenerStatusThread.Start();
            
            LogEvent("Listener Status Thread started", EventLogEntryType.Information);

            HttpListener listener = new HttpListener();
            listener.Prefixes.Add("http://*:4567/");

            while (true)
                try
                {
                    listener.Start();
                    //LogEvent("Listener 4567 started", EventLogEntryType.Information);

                    //System.Threading.Thread.Sleep(100);

                    HttpListenerContext context = listener.GetContext();
                    HttpListenerRequest request = context.Request;
                    context.Response.AppendHeader("Access-Control-Allow-Origin", "*");
                    context.Response.StatusCode = 400;

                    string url = context.Request.RawUrl;
                    string no_host = url.Remove(0, 1);
                    string[] url_array = no_host.Split('/');

                    int command = 0;// default if none given

                    try
                    {
                        command = Int32.Parse(url_array[0]);
                    }
                    catch
                    {
                        //TODO catch code
                    }

                    if (command == 0)
                    {
                        context.Response.ContentType = "text/html";
                        byte[] buffer = Encoding.UTF8.GetBytes("555.55.555");
                        context.Response.ContentLength64 = buffer.Length;
                        using (Stream s = context.Response.OutputStream)
                            s.Write(buffer, 0, buffer.Length);
                    }
                    else if (command == 1)
                    {
                        int bed = 0;
                        int min = 0;
                        int dla = 3;

                        try
                        {
                            bed = Int32.Parse(url_array[1]);
                            min = Int32.Parse(url_array[2]);
                            dla = Int32.Parse(url_array[3]);
                        }
                        catch
                        {
                            //TODO catch code
                        }

                        threadStop.Reset();//stop listnerStatusThread from checking tmax status

                        mainThreadStop.WaitOne(1000);//wait until oThread tmax.status in progress completes or 1 sec passes

                        if (tmax.activate((byte)(bed), (byte)(min), (byte)(dla)))
                        {
                            context.Response.StatusCode = 201;
                        }

                        threadStop.Set();//resume oThread checking tmax.status
                    }
                    else if (command == 2)
                    {
                        int bed = 0;

                        try
                        {
                            bed = Int32.Parse(url_array[1]);
                        }
                        catch
                        {
                            //TODO catch code
                        }

                        threadStop.Reset();//stop listnerStatusThread from checking tmax status

                        mainThreadStop.WaitOne(1000);//wait until oThread tmax.status in progress completes or 1 sec passes

                        if (tmax.activate((byte)(bed), 0, 0))
                        {
                            context.Response.StatusCode = 201;
                        }

                        threadStop.Set();//resume oThread checking tmax.status
                    }

                    context.Response.Close();
                }
                catch(Exception e)
                {
                    LogEvent("Main Webserver 4567 Error. " + e, EventLogEntryType.Error);
                }
        }

        static void statusListener()
        {
            HttpListener listener = new HttpListener();
            listener.Prefixes.Add("http://*:4568/");

            while (true)
                try
                {
                    listener.Start();
                    //LogEvent("Listener 4568 started", EventLogEntryType.Information);

                    //System.Threading.Thread.Sleep(100);

                    HttpListenerContext context = listener.GetContext();
                    HttpListenerRequest request = context.Request;
                    context.Response.AppendHeader("Access-Control-Allow-Origin", "*");
                    context.Response.ContentType = "text/javascript";
                    context.Response.StatusCode = 200;

                    string url = context.Request.RawUrl;
                    string no_host = url.Remove(0, 1);
                    string[] url_array = no_host.Split('/');

                    int number_of_beds = 15;// default if none given

                    try
                    {
                        number_of_beds = Int32.Parse(url_array[0]);
                    }
                    catch
                    {
                        context.Response.StatusCode = 400;
                    }

                    int[] stbuff = tmax.status_buffer_accessor();

                    /*foreach (int st in stbuff)
                    {
                        Console.Write(st.ToString() + " "); 
                    }*/

                    List<int> status = new List<int>();

                    for (int i = 6; i < number_of_beds + 6; i++) //length determined by number of beds
                    {
                        status.Add(stbuff[i]);
                    }

                    //Console.WriteLine("[]");

                    int[] tbuff = tmax.time_buffer_accessor();

                    /*foreach (int t in tbuff)
                    {
                        Console.Write(t.ToString() + " ");
                    }*/

                    List<int> time = new List<int>();

                    for (int i = 6; i < (number_of_beds * 2 + 6); i += 2) //length determined by number of beds
                    {
                        time.Add(tbuff[i] + tbuff[i + 1] * 256);//255 or 256 not sure
                    }

                    List<string> time_status = new List<string>();

                    for (int i = 0; i < number_of_beds; i++)
                    {
                        string n = (i + 1).ToString();
                        string s = status[i].ToString();
                        string t = time[i].ToString();

                        time_status.Add("{\"number\": \"" + n + "\", \"status\": \"" + s + "\", \"time\": " + t + "}");
                    }

                    string time_status_string = string.Join(",", time_status.ToArray());

                    time_status_string = "[" + time_status_string + "]";

                    byte[] buffer = Encoding.UTF8.GetBytes(time_status_string);
                    context.Response.ContentLength64 = buffer.Length;
                    using (Stream s = context.Response.OutputStream)
                        s.Write(buffer, 0, buffer.Length);

                    //Console.WriteLine("\n status inquiry");

                    context.Response.Close();
                }
                catch(Exception e)
                {
                    LogEvent("Status Listener 4568 Error " + e, EventLogEntryType.Error);
                }
        }

        static void status()
        {
            while (true)
            {
                try
                {
                    threadStop.WaitOne(1000);

                    mainThreadStop.Reset();
                    //Console.WriteLine("status ----");
                    tmax.status();
                    //Console.WriteLine("status ----");
                    mainThreadStop.Set();

                    threadStop.WaitOne(1000);

                    mainThreadStop.Reset();
                    //Console.WriteLine("time ----");
                    tmax.time();
                    //Console.WriteLine("time ----");
                    mainThreadStop.Set();
                    //Console.WriteLine("---");
                    //foreach (int t in tmax.time_buffer_accessor())
                    //{
                    //Console.Write(t.ToString() + ",");
                    //}
                    //Console.WriteLine("status or time working fine");
                }
                catch
                {
                    //Console.WriteLine("status or time not working");
                    //Thread.Sleep(250);
                    //tmax.clear_cache();
                }
            }
        }
    }
}