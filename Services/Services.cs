using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ServiceProcess;
using System.IO;
using System.Diagnostics;
using Chetch.Utilities;
using Chetch.Utilities.Config;
using Chetch.Messaging;

namespace Chetch.Services
{
    abstract public class ChetchService : ServiceBase
    {
        protected readonly String EVENT_LOG_NAME = null;
        protected TraceSource Tracing { get; set; } = null;

        public ChetchService(String traceSourceName, String logName)
        {
            if (logName != null)
            {
                EVENT_LOG_NAME = logName;
                if (!AppConfig.VerifyEventLogSources(EVENT_LOG_NAME))
                {
                    throw new Exception("Newly created event log sources.  Restart required");
                }
            }
            Tracing = TraceSourceManager.GetInstance(traceSourceName);
            Tracing?.TraceEvent(TraceEventType.Information, 0, "Created service with trace source {0} and event log {1}", traceSourceName, logName);
        }

        public void TestStart(string[] args = null)
        {
            OnStart(args);
        }

        public void TestStop()
        {
            OnStop();
        }
    }

    abstract public class ChetchMessagingServer : ChetchService
    {
        protected Server MServer { get; set; } = null;
        
        public ChetchMessagingServer(String traceSourceName, String logName) : base(traceSourceName, logName)
        {

        }

        /// <summary>
        /// Override this method to provide specific server instance and configure  server tracing
        /// </summary>
        /// <returns></returns>
        abstract protected Server CreateServer();
        
        override protected void OnStart(string[] args)
        {
            Tracing?.TraceEvent(TraceEventType.Information, 0, "Starting service {0}", ServiceName);
            base.OnStart(args);

            Tracing?.TraceEvent(TraceEventType.Information, 0, "Creating messaging server");
            MServer = CreateServer();
            Tracing?.TraceEvent(TraceEventType.Information, 0, "Created messaging server: {0}", MServer.ID);

            Tracing?.TraceEvent(TraceEventType.Information, 0, "Starting messaging server {0}", MServer.ID);
            MServer.Start();
            Tracing?.TraceEvent(TraceEventType.Information, 0, "Started messaging server {0}", MServer.ID);

            Tracing?.TraceEvent(TraceEventType.Information, 0, "Started service {0}", ServiceName);
        }

        override protected void OnStop()
        {
            Tracing?.TraceEvent(TraceEventType.Information, 0, "Stopping service {0}", ServiceName);
            base.OnStop();

            Tracing?.TraceEvent(TraceEventType.Information, 0, "Stopping messaging server {0}", MServer.ID);
            MServer.Stop();
            Tracing?.TraceEvent(TraceEventType.Information, 0, "Stopped messaging server {0}", MServer.ID);

            Tracing?.TraceEvent(TraceEventType.Information, 0, "Stopped service {0}", ServiceName);
        }
    } //end ChetchMessagingService


    abstract public class ChetchMessagingClient : ChetchService
    {
        protected ClientConnection Client { get; set; } = null;
        protected String ClientName { get; set; } = null;

        abstract protected ClientConnection ConnectClient();
        abstract public void HendleClientMessage(Connection cnn, Message message);
        abstract public void HandleClientError(Connection cnn, Exception e);

        public ChetchMessagingClient(String traceSourceName, String logName) : base(traceSourceName, logName)
        {

        }

        override protected void OnStart(string[] args)
        {
            Tracing?.TraceEvent(TraceEventType.Information, 0, "Starting service {0}", ServiceName);
            base.OnStart(args);

            Tracing?.TraceEvent(TraceEventType.Information, 0, "Connecting client to server");
            Client = ConnectClient();
            Client.HandleMessage += HendleClientMessage;
            Client.HandleError += HandleClientError;


            Tracing?.TraceEvent(TraceEventType.Information, 0, "Connected client {0} to server {1}", Client.Name, Client.ServerID);

            Tracing?.TraceEvent(TraceEventType.Information, 0, "Started service {0}", ServiceName);
        }

        override protected void OnStop()
        {
            Tracing?.TraceEvent(TraceEventType.Information, 0, "Stopping service {0}", ServiceName);
            base.OnStop();

            Tracing?.TraceEvent(TraceEventType.Information, 0, "Closing client {0}", Client.Name);
            Client.Close();
            Tracing?.TraceEvent(TraceEventType.Information, 0, "Closed client {0}", Client.Name);

            Tracing?.TraceEvent(TraceEventType.Information, 0, "Stopped service {0}", ServiceName);
        }
    }
}
