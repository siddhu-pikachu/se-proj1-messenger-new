﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using MessengerDashboard.Sentiment;
using MessengerDashboard.Summarization;
using MessengerNetworking.Communicator;
using System.Net;
using MessengerNetworking.NotificationHandler;
using MessengerDashboard.Telemetry;
using System.Diagnostics;
using System.Threading;
using MessengerDashboard.Server.Events;
using MessengerDashboard.Client;
using Microsoft.Extensions.Configuration.UserSecrets;
using MessengerNetworking.Factory;

namespace MessengerDashboard.Server
{
    /// <summary>
    /// Handles the server session.
    /// </summary>
    /// <remarks>
    /// Implements <see cref="IServerSessionController"/>
    /// </remarks>
    public class ServerSessionController : IServerSessionController    
    {
        public Analysis _sessionAnalytics;

        public SessionInfo _sessionInfo = new();

        private readonly ICommunicator _communicator;

        private readonly string _moduleIdentifier = "Dashboard";

        private readonly ISentimentAnalyzer _sentimentAnalyzer = SentimentAnalyzerFactory.GetSentimentAnalyzer();

        private readonly Serializer _serializer = new();

        private readonly SessionMode _sessionMode;

        private readonly ITextSummarizer _textSummarizer = TextSummarizerFactory.GetTextSummarizer();

        private TextSummary? _chatSummary;

        private int _clientCount = 0;

        /// <summary>
        /// Initializes a new instance of the <see cref="ServerSessionController"/> with the provided <see cref="ICommunicator"/> instance.
        /// </summary>
        /// <param name="communicator">An <see cref="ICommunicator "/> implementation for server communication.</param>
        public ServerSessionController(ICommunicator communicator)
        {
            _communicator = communicator;
            _communicator.AddSubscriber(_moduleIdentifier, this);
            ConnectionDetails = new(_communicator.IpAddress, _communicator.ListenPort);
        }
        
        public ServerSessionController()
        {
            _communicator = Factory.GetInstance();
            _communicator.AddSubscriber(_moduleIdentifier, this);
            ConnectionDetails = new(_communicator.IpAddress, _communicator.ListenPort);
        }

        public event EventHandler NewUserAdded;

        public event EventHandler SessionEnded;

        public event EventHandler<SessionUpdatedEventArgs> SessionUpdated;

        //     Returns the credentials required to Join the meeting
        public ConnectionDetails ConnectionDetails { get; private set; } = null;

        public string UserName { get; set; }

        public string UserEmail { get; set; }

        public string UserPhotoUrl { get; set; }

        public SessionMode SessionMode { get; private set; }
        public void BroadcastPayloadToClients(Operation operation, SessionInfo? sessionInfo, TextSummary? summary = null,
                                                      Analysis? sessionAnalytics = null, ClientInfo? user = null)
        {
            ServerPayload serverPayload;
            lock (this)
            {
                serverPayload = new ServerPayload(operation, sessionInfo, summary, sessionAnalytics, user);
                string serializedData = _serializer.Serialize(serverPayload);
                _communicator.Broadcast(_moduleIdentifier, serializedData);
            }
            Trace.WriteLine("Dashboard: Data sent to specific client");

        }

        public void DeliverPayloadToClient(Operation operation, string ip, int port, SessionInfo? sessionInfo,
                    TextSummary? summary = null, Analysis? sessionAnalytics = null,
                    ClientInfo? user = null)
        {
            ServerPayload serverPayload;
            lock (this)
            {
                serverPayload = new ServerPayload(operation, sessionInfo, summary, sessionAnalytics, user);
                string serializedData = _serializer.Serialize(serverPayload);
                _communicator.SendMessage(ip, port, _moduleIdentifier, serializedData);
            }
            Trace.WriteLine("Dashboard: Data sent to specific client");
        }

        /// <summary>
        /// Safely ends the meeting.
        /// </summary>
        public void EndMeet()
        {
            // Calculate the summary from the chats
            // Save the summary
        }

        public void EndSession()
        {
            BroadcastPayloadToClients(Operation.EndSession, _sessionInfo);
        }

        public void OnClientJoined(string ip, int port)
        {
            /*
             
            lock (this)
            {
                _clientCount += 1;
                //_communicator.
                int id = _clientCount;
                UserInfo userInfo = new(null, id, null, null);
                //_communicator.
                //_communicator.AddClient();
                DeliverPayloadToClient(Operation.ID, null, null, null, userInfo, id);
            }
           */
        }

        public void OnClientLeft(string ip, int port)
        {
            // TODO: Remove Client
        }

        public void OnDataReceived(string serializedData)
        {
            
            if (serializedData == null)
            {
                throw new ArgumentNullException("Null data received");
            }
            ClientPayload clientPayload = _serializer.Deserialize<ClientPayload>(serializedData);

            if (clientPayload == null || clientPayload.UserName == null)
            {
                throw new ArgumentNullException("Null user received");
            }
            Operation operationType = clientPayload.Operation;
            switch (operationType)
            {
                case Operation.ToggleSessionMode:
                    break;
                case Operation.AddClient:
                    AddClient(clientPayload);
                    break;
                case Operation.GetSummary:
                    DeliverSummaryToClient(clientPayload);
                    break;
                case Operation.GetAnalytics:
                    DeliverAnalyticsToClient(clientPayload);
                    break;
                case Operation.RemoveClient:
                    RemoveClient(clientPayload);
                    break;
                case Operation.EndSession:
                    RemoveClient(clientPayload);
                    break;
                default:
                    break;
            }
        }
        private void AddClient(ClientPayload clientPayload)
        {
            lock (this)
            {
                _clientCount += 1;
                int id = _clientCount;
                ClientInfo user = new() { ClientEmail = clientPayload.UserEmail, ClientId = id, ClientName = clientPayload.UserName,
                                        ClientPhotoUrl = clientPayload.UserPhotoURL };
                _sessionInfo.Users.Add(user);
                _communicator.AddClient(clientPayload.IpAddress, clientPayload.Port);
                DeliverPayloadToClient(Operation.AddClientACK, clientPayload.IpAddress, clientPayload.Port, _sessionInfo, null, null, user);
                NewUserAdded?.Invoke(this, EventArgs.Empty);
            }
       }
        /*

        //    Telemetry will Subscribes to changes in the session object
        public void Subscribe(ITelemetryNotifications listener)
        {
            lock (this)
            {
                _telemetrySubscribers.Add(listener);
            }
        }


        */
        private TextSummary CreateSummary()
        {
            Trace.WriteLine("Dashboard: Getting chats");
            // TODO : GET chats
            Trace.WriteLine("Dashboard: Creating Summary");
            string[] chats = new string[] { string.Empty };
            TextSummarizationOptions options = new();
            _chatSummary = _textSummarizer.Summarize(chats, options);
            Trace.WriteLine("Dashboard: Created Summary");
            return _chatSummary;
        }

        private void DeliverAnalyticsToClient(ClientPayload receivedObject)
        {
            ClientInfo user = new(receivedObject.UserName, receivedObject.UserID, receivedObject.UserEmail, receivedObject.UserPhotoURL);
            try
            {
                //var allChats = _contentServer.GetAllMessages().ToArray();
                //_sessionAnalytics = _telemetry.GetTelemetryAnalytics(allChats);
                DeliverPayloadToClient(Operation.GetAnalytics, receivedObject.IpAddress, receivedObject.Port, null, null, _sessionAnalytics, user);
            }
            catch (Exception)
            {
                // In case of a failure, the user is returned a null object
                //SendDataToClient("getAnalytics", null, null, null, user);
            }
        }

        private void DeliverSummaryToClient(ClientPayload clientPayload)
        {
            TextSummary summaryData = CreateSummary();
            ClientInfo user = new(clientPayload.UserName, clientPayload.UserID, clientPayload.UserEmail, clientPayload.UserPhotoURL);
            Trace.WriteLine("Dashboard: Sending summary to client");
            DeliverPayloadToClient(Operation.GetSummary, clientPayload.IpAddress, clientPayload.Port, null, summaryData, null, user);
        }

        private void GetAnalytics(ClientPayload receivedObject)
        {
            ClientInfo user = new(receivedObject.UserName, receivedObject.UserID,
                receivedObject.UserEmail, receivedObject.UserPhotoURL);
            // TODO : Analysis of all chats
            DeliverPayloadToClient(Operation.GetAnalytics, receivedObject.IpAddress, receivedObject.Port, null, null, null, user);
        }

        private void RemoveClient(ClientPayload receivedObject)
        {
            Trace.WriteLine("Dashboard: Removing Client");
            _communicator.RemoveClient(receivedObject.IpAddress, receivedObject.Port);
            int removedCount = _sessionInfo.Users.RemoveAll(user => user.ClientId == receivedObject.UserID);
            if (removedCount != 0)
            {
                SessionUpdated?.Invoke(this, new(_sessionInfo));
            }
            DeliverPayloadToClient(Operation.RemoveClient, receivedObject.IpAddress, receivedObject.Port, _sessionInfo);
        }

        private void ToggleSessionMode()
        {
            Trace.WriteLine("Dashboard: Session Mode changed in Session Data");
            BroadcastPayloadToClients(Operation.ToggleSessionMode, _sessionInfo);
        }
    }
}