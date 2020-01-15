using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ServiceProcess;
using System.IO;
using System.Diagnostics;
using Chetch.Utilities;

namespace Chetch.Services
{
    
    public class ServiceLog
    {
        public const int EVENT_LOG = 1;
        public const int CONSOLE = 2;

        private EventLog _log;
        public int Options { get; set; }
        public String Source
        {
            get
            {
                return _log.Source;
            }
            set
            {
                _log.Source = value;
            }
        }
        public String Log
        {
            get
            {
                return _log.Log;
            }
            set
            {
                _log.Log = value;
            }
        }

        public ServiceLog(int logOptions = EVENT_LOG)
        {
            Options = logOptions;
            _log = new EventLog();
        }

        public void WriteEntry(String entry, EventLogEntryType eventType)
        {
            if((Options & EVENT_LOG) > 0)
            {
                _log.WriteEntry(entry, eventType);
            }
            
            if ((Options & CONSOLE) > 0)
            {
                Console.WriteLine(eventType + ": " + entry);
            }
        }

        public void WriteError(String entry)
        {
            WriteEntry(entry, EventLogEntryType.Error);
        }
        public void WriteInfo(String entry)
        {
            WriteEntry(entry, EventLogEntryType.Information);
        }
        public void WriteWarning(String entry)
        {
            WriteEntry(entry, EventLogEntryType.Warning);
        }
    }

    abstract public class ChetchService : ServiceBase
    {
        protected ServiceLog Log { get; set; }

        public ChetchService()
        {
            Log = new ServiceLog();
            Log.Options = ServiceLog.EVENT_LOG;
        }
    }

    //base client class
    abstract public class ChetchServiceClient
    {
       
    }

    //base service message class
    public class ServiceMessage : NamedPipeManager.Message
    {
        public ServiceMessage()
        {
            //parameterless constructor required for xml serializing
        }

        public ServiceMessage(NamedPipeManager.MessageType type = NamedPipeManager.MessageType.NOT_SET) : base(type)
        {
            
        }

        public ServiceMessage(String message, int subType = 0, NamedPipeManager.MessageType type = NamedPipeManager.MessageType.NOT_SET) : base(message, subType, type)
        {
            
        }

        public ServiceMessage(String message, NamedPipeManager.MessageType type = NamedPipeManager.MessageType.NOT_SET) : this(message, 0, type)
        {
            //empty
        }
    }
}
