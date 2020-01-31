using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Chetch.Utilities;
using System.IO;
using System.IO.Pipes;
using System.Collections.Concurrent;
using System.Diagnostics;


namespace Chetch.Services
{
    abstract public class NamedPipeService<M> : ChetchService where M : ServiceMessage, new()
    {
        private String _inboundID; //used by clients to send messages to the service (set in concrete class)

        //Pipes out ... dynamically added by request from client
        private ConcurrentDictionary<String, NamedPipeServerStream> _pipesOut;
        private ConcurrentDictionary<String, StreamWriter> _streamWriters;

        //One and only one pipe in
        private NamedPipeServerStream _pipeIn;


        public NamedPipeService(String inboundID)
        {
            _inboundID = inboundID;

            PipeSecurity security = NamedPipeManager.GetSecurity(NamedPipeManager.SECURITY_EVERYONE);
            _pipeIn = NamedPipeManager.Create(_inboundID, PipeDirection.In, security, this.OnClientConnectInbound);
            if (_pipeIn == null) throw new IOException("Failed to create inbound pipe server " + _inboundID);
            
            _pipesOut = new ConcurrentDictionary<string, NamedPipeServerStream>();
            _streamWriters = new ConcurrentDictionary<string, StreamWriter>();
        }

        private void CreatePipeOut(String pipeName)
        {
            if (_pipesOut.ContainsKey(pipeName)) throw new IOException("CreatePipeOut: " + pipeName + " already exists");
            PipeSecurity security = NamedPipeManager.GetSecurity(NamedPipeManager.SECURITY_EVERYONE);
            NamedPipeServerStream stream = NamedPipeManager.Create(pipeName, PipeDirection.Out, security, this.OnClientConnectOutbound);
            _pipesOut[pipeName] = stream;
        }

        virtual public void Send(M message, String pipeName)
        {
            if (!_pipesOut.ContainsKey(pipeName))
            {
                throw new Exception("Send: no output pipe with name " + pipeName);
            }

            var stream = _pipesOut[pipeName];
            
            if (stream.IsConnected)
            {
                try
                {
                    if (!_streamWriters.ContainsKey(pipeName) || _streamWriters[pipeName] == null)
                    {
                        var sw = new StreamWriter(stream);
                        sw.AutoFlush = true;
                        _streamWriters[pipeName] = sw;
                    }

                    message.Serialize(_streamWriters[pipeName]);
                }
                catch (Exception e)
                {
                    Log.WriteError("Broadcast: " + e.Message);
                }
            }
            else
            {
                ClearPipeOut(pipeName);
            }
        }

        virtual public void Broadcast(String data)
        {
            var message = new M();
            message.Type = NamedPipeManager.MessageType.NOT_SET;
            message.Add(data);
            Broadcast(message);
        }

        virtual public void Broadcast(M message)
        {
            var exceptions = new List<Exception>();
            foreach (String pipeName in _pipesOut.Keys)
            {
                try
                {
                    Send(message, pipeName);
                } catch (Exception e)
                {
                    exceptions.Add(e);
                }
            }
            if(exceptions.Count > 0)
            {
                throw new AggregateException(exceptions);
            }
        }

        virtual protected M CreateResponse(M message, NamedPipeManager.MessageType responseType)
        {
            var response = new M();
            response.Type = responseType;
            response.ResponseID = message.ID;
            return response;
        }

        virtual protected M CreatePingResponse(M message)
        {
            var response = CreateResponse(message, NamedPipeManager.MessageType.PING_RESPONSE);
            var sender = message.Value;
            response.Add(sender);

            return response;
        }

        virtual protected M CreateStatusResponse(M message)
        {
            var response = CreateResponse(message, NamedPipeManager.MessageType.STATUS_RESPONSE);
            return response;
        }

        virtual protected M CreateError(Exception e, M message)
        {
            var error = CreateResponse(message, NamedPipeManager.MessageType.ERROR);
            error.Value = e.Message;
            return error;
        }

        virtual protected void HandleReceivedMessage(M message)
        {
            switch (message.Type)
            {
                case NamedPipeManager.MessageType.REGISTER_LISTENER:
                    String pipeName = message.Sender;
                    try
                    {
                        CreatePipeOut(pipeName);
                        Log.WriteInfo("Created pipe out: " + pipeName);
                    }
                    catch (Exception e)
                    {
                        Log.WriteError("Error creating " + pipeName + ": " + e.Message);
                    }
                    break;

                case NamedPipeManager.MessageType.PING:
                    try
                    {
                        var response = CreatePingResponse(message);
                        Send(response, message.Sender);
                    }
                    catch (Exception e)
                    {
                        Log.WriteError("Error attempting to response to Ping: " + e.Message);
                    }
                    break;

                case NamedPipeManager.MessageType.STATUS_REQUEST:
                    try
                    {
                        var response = CreateStatusResponse(message);
                        Send(response, message.Sender);
                    }
                    catch (Exception e)
                    {
                        Log.WriteError("Error attempting to response to status request: " + e.Message);
                    }
                    break;


                case NamedPipeManager.MessageType.COMMAND:
                    try
                    {
                        OnCommandReceived(message);
                    }
                    catch (Exception e)
                    {
                        Broadcast(CreateError(e, message));
                    }
                    break;

                default:
                    try
                    {
                        OnMessageReceived(message);
                    } catch (Exception e)
                    {
                        Broadcast(CreateError(e, message));
                    }
                    break;
            }
        }


        //override these in child classes, OnMessageReceived picks up everything except those defined in HandlereceivedMessage
        abstract protected void OnCommandReceived(M message);
        abstract protected void OnMessageReceived(M message);
        
        private int OnClientConnectInbound(NamedPipeManager.PipeInfo pipeInfo)
        {
            try
            {
                NamedPipeServerStream stream = (NamedPipeServerStream)pipeInfo.Stream;
                if (stream != _pipeIn)
                {
                    ClearPipeIn(stream);
                }

                using (StreamReader sr = new StreamReader(stream))
                {
                    while (stream.IsConnected)
                    {
                        // Display the read text to the console
                        string temp;
                        while ((temp = sr.ReadLine()) != null)
                        {
                            var message = ServiceMessage.Deserialize<M>(temp);
                            HandleReceivedMessage(message);
                        }
                    }
                }
            }
            // Catch the IOException that is raised if the pipe is broken
            // or disconnected.
            catch (IOException e)
            {
                Log.WriteError("OnClientConnectInbound: " + e.Message);
            }
            return NamedPipeManager.WAIT_FOR_NEXT_CONNECTION;
        }


        private int OnClientConnectOutbound(NamedPipeManager.PipeInfo pipeInfo)
        {
            try
            {
                NamedPipeServerStream stream = (NamedPipeServerStream)pipeInfo.Stream;

                if (stream != _pipesOut[pipeInfo.Name])
                {
                    ClearPipeOut(pipeInfo.Name, stream);
                }
                while (stream.IsConnected)
                {
                    //just wait...
                    System.Threading.Thread.Sleep(100);
                }
            }
            // Catch the IOException that is raised if the pipe is broken
            // or disconnected.
            catch (IOException e)
            {
                Log.WriteError("OnClientConnectionOutbound: " + e.Message);
            }
            return NamedPipeManager.WAIT_FOR_NEXT_CONNECTION;
        }

        protected void ClearPipesOut()
        {
            _pipesOut.Clear();
        }

        protected void ClearPipeOut(String pipeName, NamedPipeServerStream newPipe = null)
        {
            NamedPipeServerStream pipe;
            if (_pipesOut.TryRemove(pipeName, out pipe))
            {
                pipe.Close();
                pipe.Dispose();
            }
            if (newPipe != null)
            {
                _pipesOut[pipeName] = newPipe;
            }

            StreamWriter sw;
            _streamWriters.TryRemove(pipeName, out sw);
        }

        protected void ClearPipeIn(NamedPipeServerStream newPipe = null)
        {
            _pipeIn.Close();
            _pipeIn.Dispose();
            _pipeIn = newPipe;
        }

        new public void Dispose()
        {
            ClearPipesOut();
            ClearPipeIn();
            base.Dispose();
        }
    }
}
