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
using System.Threading;
using System.Globalization;
using System.Timers;

namespace Chetch.Services
{
    abstract public class ChetchService : ServiceBase
    {
        protected static String SUPPORTED_CULTURES = "en-GB,en-US";

        static protected bool IsSupportedCulture(CultureInfo ci)
        {
            if (SUPPORTED_CULTURES == null || SUPPORTED_CULTURES.Length == 0) return true;

            String[] cultures = SUPPORTED_CULTURES.Split(',');
            return cultures.Contains(ci.Name);
        }
        
        protected readonly String EVENT_LOG_NAME = null;
        protected TraceSource Tracing { get; set; } = null;

        protected System.Configuration.ApplicationSettingsBase Settings { get; set; } = null;

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
            
            if (traceSourceName != null)
            {
                Tracing = TraceSourceManager.GetInstance(traceSourceName);
                Tracing?.TraceEvent(TraceEventType.Information, 0, "Created service with trace source {0} and event log {1}", traceSourceName, logName);
            }

            try
            {
                CultureInfo defaultCultureInfo = System.Globalization.CultureInfo.DefaultThreadCurrentCulture;
                CultureInfo currentCultureInfo = Thread.CurrentThread.CurrentCulture;
                if (defaultCultureInfo == null && currentCultureInfo == null) throw new Exception("Both default and current CultureInfo are null!");
                Tracing?.TraceEvent(TraceEventType.Information, 0, "Current CultureInfo {0}, Default CultureInfo {1}", currentCultureInfo.Name, defaultCultureInfo?.Name);
                if ((currentCultureInfo != null && !IsSupportedCulture(currentCultureInfo)) || (defaultCultureInfo != null && !IsSupportedCulture(defaultCultureInfo)))
                {
                    String cultureName = SUPPORTED_CULTURES.Split(',')[0];
                    Tracing?.TraceEvent(TraceEventType.Warning, 0, "CultureInfo is not supported so changing to {0}", cultureName);

                    CultureInfo supportedCultureInfo = new CultureInfo(cultureName);
                    Thread.CurrentThread.CurrentCulture = supportedCultureInfo;
                    System.Globalization.CultureInfo.DefaultThreadCurrentCulture = supportedCultureInfo;
                }
                Tracing?.TraceEvent(TraceEventType.Information, 0, "Current CultureInfo {0}, Default CultureInfo {1}", Thread.CurrentThread.CurrentCulture?.Name, System.Globalization.CultureInfo.DefaultThreadCurrentCulture?.Name);
            } catch (Exception e)
            {
                Tracing.TraceEvent(TraceEventType.Error, 0, e.Message);
                throw e;
            }
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
            }
            catch (Exception e)
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
        protected const String CMC_AUTH_TOKEN_SETTINGS_KEY = "CMCAuthToken";
        protected const String CMC_AUTH_TOKEN_ENCRYPTION_SEED = "AT";

        protected ClientConnection Client { get; set; } = null;
        protected String ClientName { get; set; } = null;
        private String connectionString; //can be set in service arguments
        private System.Timers.Timer _connectTimer;
        private List<MessageFilter> _subscriptions = new List<MessageFilter>();
        protected List<String> CommandHelp = new List<String>();


        abstract protected ClientConnection ConnectClient(String clientName, String connectionString, String authToken);
        abstract public bool HandleCommand(Connection cnn, Message message, String command, List<ValueType> args, Message response);
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

            connectionString = args != null && args.Length > 0 ? args[0] : null;
            Tracing?.TraceEvent(TraceEventType.Information, 0, "Start timer to connect client to server {0}", connectionString == null ? "localhost" : connectionString);
            _connectTimer = new System.Timers.Timer();
            _connectTimer.Interval = 2000;
            _connectTimer.Elapsed += new ElapsedEventHandler(HandleConnectTimer);
            _connectTimer.Start();

            Tracing?.TraceEvent(TraceEventType.Information, 0, "Started service {0}", ServiceName);
            
        }

        override protected void OnStop()
        {
            Tracing?.TraceEvent(TraceEventType.Information, 0, "Stopping service {0}", ServiceName);
            base.OnStop();

            if (Client != null)
            {
                Tracing?.TraceEvent(TraceEventType.Information, 0, "Closing client {0}", Client.Name);
                Client.Close();
                Tracing?.TraceEvent(TraceEventType.Information, 0, "Closed client {0}", Client.Name);
            }

            if (_connectTimer != null) _connectTimer.Stop();

            Tracing?.TraceEvent(TraceEventType.Information, 0, "Stopped service {0}", ServiceName);
        }

        virtual protected void OnClientConnect(ClientConnection cnn)
        {
            Tracing?.TraceEvent(TraceEventType.Information, 0, "OnClientConnect: {0}", cnn?.Name);

            if(cnn.AuthToken != null)
            {
                if (Settings == null)
                {
                    Tracing?.TraceEvent(TraceEventType.Warning, 0, "OnClientConnect: Connction has provided an auth token but the Settings file property is null so the token cannot be saved");
                }
                else
                {
                    try
                    {
                        //NOTE: Important! The setting must be of User Scope for this to save
                        String encryptedAuthToken = Chetch.Utilities.BasicEncryption.Encrypt(cnn.AuthToken, CMC_AUTH_TOKEN_ENCRYPTION_SEED);
                        Settings[CMC_AUTH_TOKEN_SETTINGS_KEY] = encryptedAuthToken;
                        Settings.Save();
                    } catch (Exception e)
                    {
                        Tracing?.TraceEvent(TraceEventType.Warning, 0, "OnClientConnect: Exception thrown when trying to save auth token to setting {0}: {1}", CMC_AUTH_TOKEN_SETTINGS_KEY, e.Message);
                    }
                }
            }

            if (_subscriptions.Count > 0 && cnn.Name == ClientName) {
                foreach (MessageFilter f in _subscriptions)
                {
                    Tracing?.TraceEvent(TraceEventType.Information, 0, "OnClientConnect: Subscribing to {0}", f.Sender);
                    cnn.Subscribe(f);
                }
            }
        }

        private void HandleConnectTimer(Object sender, ElapsedEventArgs ea)
        {
            _connectTimer.Stop();
            try
            {
                Tracing?.TraceEvent(TraceEventType.Information, 0, "Trying to connect client {0} to {1}", ClientName, connectionString);
                String authToken = null;
                if(Settings != null)
                {
                    try
                    {
                        String encryptedAuthToken = Settings[CMC_AUTH_TOKEN_SETTINGS_KEY]?.ToString();
                        if (String.IsNullOrEmpty(encryptedAuthToken))
                        {
                            authToken = null;
                            Tracing?.TraceEvent(TraceEventType.Warning, 0, "If auth token setting persists in being empty ensure that the scope is set to User");
                        } else
                        {
                            authToken = Chetch.Utilities.BasicEncryption.Decrypt((String)encryptedAuthToken, CMC_AUTH_TOKEN_ENCRYPTION_SEED);
                            Tracing?.TraceEvent(TraceEventType.Information, 0, "Successfully retrieved auth token");

                        }
                    } 
                    catch (Exception e)
                    {
                        Tracing?.TraceEvent(TraceEventType.Error, 0, "Exception when getting setting {0}: {1}", CMC_AUTH_TOKEN_SETTINGS_KEY, e.Message);
                    }
                }

                Client = ConnectClient(ClientName, connectionString, authToken);
                Client.Context = ClientConnection.ClientContext.SERVICE;
                Client.HandleMessage += HandleClientMessage;
                Client.ModifyMessage += ModifyClientMessage;
                Client.HandleError += HandleClientError;

                Tracing?.TraceEvent(TraceEventType.Information, 0, "Connected client {0} to server {1}", Client.Name, Client.ServerID);
            }
            catch (Exception e)
            {
                var ie = e.InnerException;
                Tracing?.TraceEvent(TraceEventType.Error, 0, "{0}: {1} with inner exception {2}: {3}", e.GetType().ToString(), e.Message, ie == null ? "N/A" : ie.GetType().ToString(), ie == null ? "N/A" : ie.Message);
                _connectTimer.Start();
            }
        }

        protected bool IsSubscribedTo(String clientName)
        {
            foreach (MessageFilter f in _subscriptions)
            {
                if (f.Sender.Equals(clientName))return true;
            }
            return false;
        }

        protected void Subscribe(MessageFilter messageFilter)
        {
            if(messageFilter.Sender == null || messageFilter.Sender == String.Empty)
            {
                throw new Exception("Message filter must have a Sender value");
            }

            _subscriptions.Add(messageFilter);
            if (Client != null && Client.IsConnected) Client.Subscribe(messageFilter);
        }

        protected void Unsubscribe(String clientName)
        {
            clientName = clientName.Trim();
            List<MessageFilter> toRemove = new List<MessageFilter>();
            foreach(MessageFilter f in _subscriptions)
            {
                if (f.Sender.Equals(clientName)) toRemove.Add(f);
            }
            foreach(MessageFilter f in toRemove)
            {
                _subscriptions.Remove(f);
            }

            if (Client != null && Client.IsConnected) Client.Unsubscribe(clientName);
        }

        //derived services can add to this help list
        protected void AddCommandHelp(String command, String description)
        {
            CommandHelp.Add(String.Format("{0}: {1}", command, description));
        }


        virtual public void AddCommandHelp()
        {
            AddCommandHelp("(h)elp", "provides a list of client available service related commands");
        }

        virtual protected Message CreateResponse(Message message)
        {
            if (Client == null || !Client.IsConnected)
            {
                throw new Exception(String.Format("Cannot create response for message (type={0}) from {1} because Client is not available", message.Type, message.Sender));
            }

            MessageType mt = MessageType.NOT_SET;
            switch (message.Type)
            {
                case MessageType.COMMAND:
                    mt = MessageType.COMMAND_RESPONSE;
                    break;
            }

            Message response = Client.CreateResponse(message, mt);
            return response;
        }

        virtual public void HandleClientMessage(Connection cnn, Message message)
        {
            switch (message.Type)
            {
                case MessageType.SHUTDOWN:
                    _connectTimer.Interval = 10000;
                    _connectTimer.Start();
                    Tracing?.TraceEvent(TraceEventType.Warning, 0, "Messaging server shutdown.... attempting to reconnect with interval {0}", _connectTimer.Interval);
                    break;

                case MessageType.COMMAND:
                    var cmd = message.Value;
                    var args = message.HasValue("Arguments") && message.GetValue("Arguments") != null ? message.GetList<ValueType>("Arguments") : new List<ValueType>();

                    var response = CreateResponse(message);
                    bool respond = true;
                    switch (cmd)
                    {
                        case "h":
                        case "help":
                            CommandHelp.Clear();
                            AddCommandHelp();
                            response.AddValue("Help", CommandHelp);
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
                        SendMessage(response);
                    }
                    break;
            }
        }

        virtual public void ModifyClientMessage(Connection cnn, Message message)
        {
            //a hook for outgoing messages (can be directed or broadcast)
        }

        //wrapper for client
        virtual public void Broadcast(Message message)
        {
            if (message != null && Client != null && Client.IsConnected)
            {
                if((message.Target != null && message.Target != String.Empty) && !Client.HasSubscriber(message.Target) || !Client.CanNotify(message.Type))
                {
                    Client.SendMessage(message);
                }

                Client.Notify(message);
            }
        }

        public void SendMessage(Message message)
        {
            if(Client != null && Client.IsConnected)
            {
                Client.SendMessage(message);
            }
        }

        public void SendCommand(String target, String command, params Object[] args)
        {
            SendCommand(target, command, new List<Object>(args));
        }

        public void SendCommand(String target, String command, List<Object> args)
        {
            if (Client != null && Client.IsConnected)
            {
                Client.SendCommand(target, command, args);
            }
        }
    } //end class
}
