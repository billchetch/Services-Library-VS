using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Chetch.Messaging;
using Chetch.Utilities.Config;

namespace Chetch.Services
{
    [System.ComponentModel.DesignerCategory("Code")]
    public class TCPMessagingServer : ChetchMessagingServer
    {
        public const int CONNECTION_REQUEST_PORT = 12000;

        private String _serverSourceName;
        public TCPMessagingServer(String serverSourceName, String sourceName, String logName) : base(sourceName, logName)
        {
            _serverSourceName = serverSourceName;
        }

        protected override Server CreateServer()
        {
            TCPServer server = new TCPServer(CONNECTION_REQUEST_PORT);
            server.Tracing = TraceSourceManager.GetInstance(_serverSourceName);
            //server.HandleMessage += 
            //server.HandleError +=
            return server;
        }

    }
}
