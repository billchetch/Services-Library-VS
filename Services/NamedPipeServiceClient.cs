using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO.Pipes;
using System.Threading.Tasks;
using Chetch.Utilities;
using System.Threading;
using System.Diagnostics;
using System.IO;

namespace Chetch.Services
{
    public class NamedPipeServiceClient<M> : ChetchServiceClient, IDisposable where M : ServiceMessage, new()
    {

        //delegate or what
        public delegate void MessageReceived(M message);

        private const int CONNECT_TIMEOUT = 15000;

        private NamedPipeClientStream _pipeClientOut;
        private NamedPipeClientStream _pipeClientIn;

        private String _serviceInboundID;
        private String _listenPipeName;
        private bool _listening = false;
        private String _lastPingID;
        
        CancellationTokenSource _cancelListenTokenSource;

        public event MessageReceived OnMessageReceived;

        public NamedPipeServiceClient(String serviceInboundID)
        {
            _serviceInboundID = serviceInboundID;
            _listenPipeName = _serviceInboundID + "-" + Process.GetCurrentProcess().Id.ToString() + "-" + this.GetHashCode() + "-" + DateTime.Now.ToString("yyyyMMddHHmmssffff");
            _pipeClientIn = new NamedPipeClientStream(".", _listenPipeName, PipeDirection.In);
        }

        private NamedPipeClientStream CreatePipeOut()
        {
            return new NamedPipeClientStream(".", _serviceInboundID, PipeDirection.Out);
        }

        public void StartListening(MessageReceived messageHandler, Action<Task> OnStoppedListening)
        {
            //Send request to open a pipe out
            var message = new M();
            message.Type = NamedPipeManager.MessageType.REGISTER_LISTENER;
            message.Add(_listenPipeName);
            Send(message);
            Console.WriteLine("Requested a pipe out for " + _listenPipeName);

            _cancelListenTokenSource = new CancellationTokenSource();
            Task task = Task.Run((Action)this.Listen, _cancelListenTokenSource.Token);
            task.ContinueWith(OnStoppedListening);
            _listening = true;
            OnMessageReceived += messageHandler;
        }

        public void StopListening()
        {
            _cancelListenTokenSource.Cancel();
            _listening = false;
        }

        virtual protected void HandleReceivedMessage(M message)
        {
            //call delegate
            OnMessageReceived?.Invoke(message);
        }
        
        private void Listen()
        {
            if (!_pipeClientIn.IsConnected)
            {
                Console.WriteLine(_listenPipeName + " is trying to connect for listening ");
                _pipeClientIn.Connect(CONNECT_TIMEOUT);
                Console.WriteLine("Connected! There are currently {0} pipe server instances open.", _pipeClientIn.NumberOfServerInstances);
            }

            using (StreamReader sr = new StreamReader(_pipeClientIn))
            {
                while (_pipeClientIn.IsConnected)
                {
                    if (_cancelListenTokenSource.Token.IsCancellationRequested)
                    {
                        return;
                    }

                    string temp;
                    while ((temp = sr.ReadLine()) != null)
                    {
                        if (_cancelListenTokenSource.Token.IsCancellationRequested)
                        {
                            return;
                        }
                        var message = ServiceMessage.Deserialize<M>(temp);
                        HandleReceivedMessage(message);
                    }

                    Console.Write("Sleeping for a bit...");
                    System.Threading.Thread.Sleep(100);
                }
            }

            Console.WriteLine("Finished listening");
        }

        virtual public void Send(ServiceMessage message)
        {
            if(_pipeClientOut == null)
            {
                _pipeClientOut = CreatePipeOut();
            }
            
            _pipeClientOut.Connect(CONNECT_TIMEOUT);

            using (var sw = new StreamWriter(_pipeClientOut))
            {
                sw.AutoFlush = true;
                message.Serialize(sw);
                
                sw.Close();
                sw.Dispose();
            }

            ClosePipe(_pipeClientOut);
            _pipeClientOut = null;
        }

        virtual public void Send(String s)
        {
            var message = new M();
            message.Type = NamedPipeManager.MessageType.NOT_SET;
            message.Add(s);
            Send(message);
        }

        public void Ping()
        {
            if(_lastPingID != null || !_listening)
            {
                return; //either waiting for last ping to return or not valid cos no listener has been attached to service
            }
            var message = new M();
            message.Type = NamedPipeManager.MessageType.PING;
            message.Add(_listenPipeName);
            _lastPingID = message.ID;
            Send(message);
        }

        private void ClosePipe(NamedPipeClientStream pipe)
        {
            if (pipe != null)
            {
                pipe.Close();
                pipe.Dispose();
            }
        }

        public void Dispose()
        {
            ClosePipe(_pipeClientIn);
            _pipeClientIn = null;

            ClosePipe(_pipeClientOut);
        }
    }
}
