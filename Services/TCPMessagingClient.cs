using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Chetch.Messaging;

namespace Chetch.Services
{
    abstract public class TCPMessagingClient : ChetchMessagingClient
    {
        private String _cmSourceName;
        public TCPMessagingClient(String clientName, String cmSourceName, String sourceName, String logName) : base(clientName, sourceName, logName)
        {
            _cmSourceName = cmSourceName;
        }

        protected override ClientConnection ConnectClient(String clientName, String connectionString)
        {
            //client manager wtih default connection localhost
            var clientMgr = new TCPClientManager(OnClientConnect);
            if (_cmSourceName != null)
            {
                clientMgr.Tracing = Chetch.Utilities.Config.TraceSourceManager.GetInstance(_cmSourceName);
            }
            clientMgr.AddServer("default", connectionString == null ? TCPServer.LocalCS(TCPMessagingServer.CONNECTION_REQUEST_PORT) : connectionString);
            var client = clientMgr.Connect(clientName, 10000);
            return client;
        }
    }
}
