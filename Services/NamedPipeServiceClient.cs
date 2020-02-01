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
    public class NamedPipeServiceClient<M, D> : ChetchServiceClient, IDisposable where M : ServiceMessage, new() where D : ServiceData<M>, new()
    {
        private const int CONNECT_TIMEOUT = 15000;

        private NamedPipeClientStream _pipeClientOut;
        private NamedPipeClientStream _pipeClientIn;

        private String _serviceInboundID;
        private String _listenPipeName;
        private bool _listening = false;
        
        CancellationTokenSource _cancelListenTokenSource;

        //delegate
        public delegate void MessageReceivedHandler(M message);
        public event MessageReceivedHandler MessageReceived;

        private D _serviceData;
        public D ServiceData { get { return _serviceData; } }

        public NamedPipeServiceClient(String serviceInboundID)
        {
            _serviceInboundID = serviceInboundID;
            _listenPipeName = _serviceInboundID + "-" + Process.GetCurrentProcess().Id.ToString() + "-" + this.GetHashCode() + "-" + DateTime.Now.ToString("yyyyMMddHHmmssffff");
            _pipeClientIn = new NamedPipeClientStream(".", _listenPipeName, PipeDirection.In);
            _serviceData = new D();
        }


        private NamedPipeClientStream CreatePipeOut()
        {
            return new NamedPipeClientStream(".", _serviceInboundID, PipeDirection.Out);
        }

        public virtual void ProcessCommandLine(String commandLine)
        {
            ProcessCommandLine(commandLine == null ? null : commandLine.Split(' '));
        }

        public virtual void ProcessCommandLine(String[] commandLine)
        {
            if(commandLine == null || commandLine.Length == 0)
            {
                throw new Exception("Command line cannot null or empty");
            }

            String target = commandLine[0];
            switch (target.ToUpper())
            {
                case "PING":
                    Ping();
                    break;

                case "STATUS":
                    RequestStatus();
                    break;

                case "ERROR":
                    ErrorTest();
                    break;

                default:
                    if (commandLine.Length >= 2)
                    {
                        String command = commandLine[1];
                        String[] args = null;
                        if (commandLine.Length > 2)
                        {
                            args = new String[commandLine.Length - 2];
                            Array.Copy(commandLine, 2, args, 0, args.Length);
                        }
                        SendCommand(target, command, args);
                    } else
                    {
                        throw new Exception("Process command line exception: incorrect number of arguments");
                    }
                    break;
            }
        }

        public void StartListening(MessageReceivedHandler messageHandler, Action<Task> OnStoppedListening)
        {
            if (_listening)
            {
                throw new Exception("Client already listening");
            }

            //Send request to open a pipe out (using a response request will populate the 'sender' property of the message
            var message = CreateResponseRequest(NamedPipeManager.MessageType.REGISTER_LISTENER);
            Send(message);
            
            _cancelListenTokenSource = new CancellationTokenSource();
            Task task = Task.Run((Action)this.Listen, _cancelListenTokenSource.Token);
            task.ContinueWith(OnStoppedListening);
            _listening = true;
            MessageReceived += messageHandler;
        }

        public void StopListening()
        {
            _cancelListenTokenSource.Cancel();
            _listening = false;
        }

        virtual protected void HandleReceivedMessage(M message)
        {
            ServiceData.LastMessageReceived = message;

            //call delegate
            MessageReceived?.Invoke(message);
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
        }

        virtual public void Send(M message)
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
                ServiceData.LastMessageSent = message;

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

        virtual protected M CreateResponseRequest(NamedPipeManager.MessageType messageType)
        {
            var message = new M();
            message.Type = messageType;
            message.Sender = _listenPipeName;
            return message;
        }

        public void Ping()
        {
            if (!_listening)
            {
                throw new Exception("Client is not listening so cannot receive Ping responses");
            }
            var message = CreateResponseRequest(NamedPipeManager.MessageType.PING);
            Send(message);
        }

        public void RequestStatus()
        {
            if (!_listening)
            {
                throw new Exception("Client is not listening so cannot receive service status report");
            }
            var message = CreateResponseRequest(NamedPipeManager.MessageType.STATUS_REQUEST);
            Send(message);
        }

        public void ErrorTest()
        {
            if (!_listening)
            {
                throw new Exception("Client is not listening so cannot receive test error message from service");
            }
            var message = CreateResponseRequest(NamedPipeManager.MessageType.ERROR_TEST);
            Send(message);
        }

        public void SendCommand(String target, String command, String[] args = null)
        {
            var cmd = new M();
            cmd.SetCommand(target, command, args);
            Send(cmd);
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
