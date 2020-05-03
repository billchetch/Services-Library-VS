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

            try
            {
                Tracing?.TraceEvent(TraceEventType.Information, 0, "Starting messaging server {0}", MServer.ID);
                MServer.Start();
                Tracing?.TraceEvent(TraceEventType.Information, 0, "Started messaging server {0}", MServer.ID);
                Tracing?.TraceEvent(TraceEventType.Information, 0, "Started service {0}", ServiceName);
            } catch (Exception e)
            {
                Tracing?.TraceEvent(TraceEventType.Error, 0, "{0} exception: {1}", e.GetType().ToString(), e.Message);
                throw e;
            }
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

        abstract protected ClientConnection ConnectClient(String clientName);
        abstract public bool HandleCommand(Connection cnn, Message message, String command, List<Object> args, Message response);
        abstract public void HandleClientError(Connection cnn, Exception e);

        public ChetchMessagingClient(String clientName, String traceSourceName, String logName) : base(traceSourceName, logName)
        {
            ClientName = clientName;
        }

        override protected void OnStart(string[] args)
        {
            if(ClientName == null)
            {
                var msg = "Cannot start service as no Client Name provided";
                Tracing?.TraceEvent(TraceEventType.Error, 0, msg);
                throw new Exception(msg);
            }

            Tracing?.TraceEvent(TraceEventType.Information, 0, "Starting service {0}", ServiceName);
            base.OnStart(args);

            Tracing?.TraceEvent(TraceEventType.Information, 0, "Connecting client to server");
            try
            {
                Client = ConnectClient(ClientName);
                Client.HandleMessage += HendleClientMessage;
                Client.HandleError += HandleClientError;

                Tracing?.TraceEvent(TraceEventType.Information, 0, "Connected client {0} to server {1}", Client.Name, Client.ServerID);
                Tracing?.TraceEvent(TraceEventType.Information, 0, "Started service {0}", ServiceName);
            }
            catch (Exception e)
            {
                var ie = e.InnerException;
                Tracing?.TraceEvent(TraceEventType.Error, 0, "{0}: {1} with inner exception {2}: {3}", e.GetType().ToString(), e.Message, ie == null ? "N/A" : ie.GetType().ToString(), ie == null ? "N/A" : ie.Message);
                throw e;
            }
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

        virtual public void AddCommandHelp(List<String> commandHelp)
        {
            commandHelp.Add("(h)elp: provides a list of client available service related commands");
        }

        virtual public void HendleClientMessage(Connection cnn, Message message)
        {
            switch (message.Type)
            {
                case MessageType.COMMAND:
                    var cmd = message.Value;
                    var args = message.HasValue("Arguments") ? message.GetList<Object>("Arguments") : new List<Object>();

                    var response = Client.CreateResponse(message, MessageType.COMMAND_RESPONSE);
                    bool respond = true;
                    switch (cmd)
                    {
                        case "h":
                        case "help":
                            List<String> help = new List<String>();
                            AddCommandHelp(help);
                            response.AddValue("Help", help);
                            break;

                        default:
                            try
                            {
                                respond = HandleCommand(cnn, message, cmd, args, response);
                            } catch (Exception e)
                            {
                                response.Type = MessageType.ERROR;
                                response.Value = e.Message;
                                respond = true;
                            }
                            break;
                    }

                    if (respond)
                    {
                        response.AddValue("OriginalCommand", cmd);
                        Client.SendMessage(response);
                    }
                    break;
            }
        }
    }
}
