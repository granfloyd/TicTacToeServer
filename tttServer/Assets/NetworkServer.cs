using UnityEngine;
using UnityEngine.Assertions;
using Unity.Collections;
using Unity.Networking.Transport;
using System.Text;
using System.IO;
using System.Collections;
using System.Collections.Generic;


public static class Data
{
    public static LinkedList<Account> accounts = new LinkedList<Account>();
}


public class Account
{
    public string username;
    public string password;
    public Account(string u, string p)
    {
        username = u;
        password = p;
    }
}
public class NetworkServer : MonoBehaviour
{
    public NetworkDriver networkDriver;
    private NativeList<NetworkConnection> networkConnections;

    NetworkPipeline reliableAndInOrderPipeline;
    NetworkPipeline nonReliableNotInOrderedPipeline;

    const ushort NetworkPort = 9001;

    const int MaxNumberOfClientConnections = 1000;

    private int currentPlayerIndex = 0;

    public char currentPlayerSymbol = 'x';

    const int usernameSignifier = 1;
    
    const char SepChar = ',';

    Dictionary<int, NetworkConnection> idToConnectionLookup;
    Dictionary<NetworkConnection, int> connectionToIDLookup;
    void Start()
    {
        idToConnectionLookup = new Dictionary<int, NetworkConnection>();
        connectionToIDLookup = new Dictionary<NetworkConnection, int>();


        networkDriver = NetworkDriver.Create();
        reliableAndInOrderPipeline = networkDriver.CreatePipeline(typeof(FragmentationPipelineStage), typeof(ReliableSequencedPipelineStage));
        nonReliableNotInOrderedPipeline = networkDriver.CreatePipeline(typeof(FragmentationPipelineStage));
        NetworkEndPoint endpoint = NetworkEndPoint.AnyIpv4;
        endpoint.Port = NetworkPort;

        int error = networkDriver.Bind(endpoint);
        if (error != 0)
            Debug.Log("Failed to bind to port " + NetworkPort);
        else
            networkDriver.Listen();

        networkConnections = new NativeList<NetworkConnection>(MaxNumberOfClientConnections, Allocator.Persistent);
    }

    public List<int> GetClientIDs()
    {
        return new List<int>(idToConnectionLookup.Keys);
    }
    static public void ConnectionEvent(int clientConnectionID)
    {
        Debug.Log("Client connection, ID == " + clientConnectionID);

    }

    static public void DisconnectionEvent(int clientConnectionID)
    {
        Debug.Log("Client disconnection, ID == " + clientConnectionID);
    }
    void OnDestroy()
    {
        networkDriver.Dispose();
        networkConnections.Dispose();
    }

    void Update()
    {
        #region Check Input and Send Msg

        if (Input.GetKeyDown(KeyCode.A))
        {
            for (int i = 0; i < networkConnections.Length; i++)
            {
                SendMessageToClient("Hello client's world, sincerely your network server", i, TransportPipeline.ReliableAndInOrder);
            }
        }

        #endregion

        networkDriver.ScheduleUpdate().Complete();

        #region Remove Unused Connections

        for (int i = 0; i < networkConnections.Length; i++)
        {
            if (!networkConnections[i].IsCreated)
            {
                networkConnections.RemoveAtSwapBack(i);
                i--;
            }
        }

        #endregion

        #region Accept New Connections

        while (AcceptIncomingConnection())
        {
            Debug.Log("Accepted a client connection");
        }

        #endregion

        #region Manage Network Events

        DataStreamReader streamReader;
        NetworkPipeline pipelineUsedToSendEvent;
        NetworkEvent.Type networkEventType;

        for (int i = 0; i < networkConnections.Length; i++)
        {
            if (!networkConnections[i].IsCreated)
                continue;

            while (PopNetworkEventAndCheckForData(networkConnections[i], out networkEventType, out streamReader, out pipelineUsedToSendEvent))
            {
                TransportPipeline pipelineUsed = TransportPipeline.NotIdentified;
                if (pipelineUsedToSendEvent == reliableAndInOrderPipeline)
                    pipelineUsed = TransportPipeline.ReliableAndInOrder;
                else if (pipelineUsedToSendEvent == nonReliableNotInOrderedPipeline)
                    pipelineUsed = TransportPipeline.FireAndForget;

                switch (networkEventType)
                {
                    case NetworkEvent.Type.Data:
                        int sizeOfDataBuffer = streamReader.ReadInt();
                        NativeArray<byte> buffer = new NativeArray<byte>(sizeOfDataBuffer, Allocator.Persistent);
                        streamReader.ReadBytes(buffer);
                        byte[] byteBuffer = buffer.ToArray();
                        string msg = Encoding.Unicode.GetString(byteBuffer);
                        ProcessReceivedMsg(msg, connectionToIDLookup[networkConnections[i]], pipelineUsed);
                        //ProcessReceivedMsg(msg);
                        buffer.Dispose();
                        break;
                    case NetworkEvent.Type.Disconnect:
                        Debug.Log("Client has disconnected from server");
                        NetworkConnection nc = networkConnections[i];
                        int id = connectionToIDLookup[nc];
                        DisconnectionEvent(id);
                        idToConnectionLookup.Remove(id);
                        connectionToIDLookup.Remove(nc);
                        networkConnections[i] = default(NetworkConnection);
                        break;
                }
            }
        }

        #endregion
    }

    private bool AcceptIncomingConnection()
    {
        NetworkConnection connection = networkDriver.Accept();
        if (connection == default(NetworkConnection))
            return false;

        int id = 0;
        while (idToConnectionLookup.ContainsKey(id))
        {
            id++;
        }
        idToConnectionLookup.Add(id, connection);
        connectionToIDLookup.Add(connection, id);

        ConnectionEvent(id);
        networkConnections.Add(connection);

        // If this is the first client that connected, it's their turn
        if (networkConnections.Length == 1)
        {
            SendMessageToClient("YOUR_TURN", 0,TransportPipeline.ReliableAndInOrder);
        }

        return true;
    }

    private bool PopNetworkEventAndCheckForData(NetworkConnection networkConnection, out NetworkEvent.Type networkEventType, out DataStreamReader streamReader, out NetworkPipeline pipelineUsedToSendEvent)
    {
        networkEventType = networkConnection.PopEvent(networkDriver, out streamReader, out pipelineUsedToSendEvent);

        if (networkEventType == NetworkEvent.Type.Empty)
            return false;
        return true;
    }

    static public void SaveData()
    {
        LinkedList<string> saveData = SerializeAccountData();

        StreamWriter writer = new StreamWriter("datafile.txt");
        foreach (string line in saveData)
            writer.WriteLine(line);
        writer.Close();
    }
    static public void LoadData()
    {
        Data.accounts.Clear();
        LinkedList<string> serializedData = new LinkedList<string>();
        string line = "";
        StreamReader sr = new StreamReader("datafile.txt");
        {

            while (!sr.EndOfStream)
            {
                line = sr.ReadLine();
                serializedData.AddLast(line);
            }
        }
        DeserializeSaveData(serializedData);
        sr.Close();
    }

    static private LinkedList<string> SerializeAccountData()
    {
        LinkedList<string> serializedData = new LinkedList<string>();
        foreach (Account account in Data.accounts)
        {
            serializedData.AddLast(usernameSignifier.ToString() + SepChar + account.username + SepChar + account.password);
        }      
        return serializedData;
    }

    static private void DeserializeSaveData(LinkedList<string> serializedData)
    {
        Account account = null;

        foreach (string line in serializedData)
        {
            string[] csv = line.Split(SepChar);
            int signifier = int.Parse(csv[0]);

            if (signifier == usernameSignifier)
            {
                account = new Account(csv[1],csv[2]);
                Data.accounts.AddLast(account);
            }
        }
    }

    private void ProcessReceivedMsg(string msg, int clientConnectionID, TransportPipeline pipeline)
    {
        Debug.Log("Msg received = " + msg);

        // Split the message into parts
        string[] msgParts = msg.Split(',');

        if (msgParts[0] == "MAKE_ACCOUNT")
        {
            // Check if an account with the same username already exists
            foreach (Account account in Data.accounts)
            {
                if (account.username == msgParts[1])
                {
                    // If it does, send a message back to the client and return
                    SendMessageToClient("Account already exists", clientConnectionID, TransportPipeline.ReliableAndInOrder);
                    return;                       
                }
            }
            // Create a new account with the provided username and password
            Account newAccount = new Account(msgParts[1], msgParts[2]);

            // Add the new account to the accounts list
            Data.accounts.AddLast(newAccount);
            // Save the updated accounts list to disk
            SaveData();
            // Send a success message back to the client
            SendMessageToClient("Account created successfully", clientConnectionID, TransportPipeline.ReliableAndInOrder);

        }
        else if (msgParts[0] == "LOGIN_DATA")
        {
            // Check if an account with the same username and password exists
            foreach (Account account in Data.accounts)
            {
                if (account.username == msgParts[1] && account.password == msgParts[2])
                {
                    // If it does, send a success message back to the client and return
                    string usernameMsg = msgParts[1];
                    SendMessageToClient("LOGIN_SUCCESSFUL" + "," + usernameMsg, clientConnectionID, TransportPipeline.ReliableAndInOrder);
                    return; 
                }
            }
            SendMessageToClient("Invalid username or password", clientConnectionID, TransportPipeline.ReliableAndInOrder); 
        }
        // If the server receives a "MOVE" message, move to the next player
        if (msgParts[0] == "MOVE")
        {
            currentPlayerIndex++;
            if (currentPlayerIndex >= networkConnections.Length)
                currentPlayerIndex = 0;

            // Switch the currentPlayerSymbol to the other player's symbol
            currentPlayerSymbol = currentPlayerSymbol == 'x' ? 'o' : 'x';

            // Send a "YOUR_TURN" message to the current player
            string turnMsg = $"YOUR_TURN,{currentPlayerSymbol}";
            SendMessageToClient(turnMsg, clientConnectionID, TransportPipeline.ReliableAndInOrder);

            // Send a "MOVE" message to all clients
            string moveMsg = $"MOVE,{msgParts[1]},{currentPlayerSymbol}";
            for (int i = 0; i < networkConnections.Length; i++)
            {
                SendMessageToClient(moveMsg, i, TransportPipeline.ReliableAndInOrder);
            }
        } 
        else if (msgParts[0] == "WINNER" || msgParts[0] == "LOSER") // If the server receives a "WINNER" message, send a "RESET" msg to all clients
        {
            // Send a "RESET" message to all clients
            string resetMsg = "RESET";           
            for (int i = 0; i < networkConnections.Length; i++)
            {
                SendMessageToClient(resetMsg, i,TransportPipeline.ReliableAndInOrder);
            }
        }

        else if (msgParts[0] == "RESET_COMPLETE")
        {
            // Send a "YOUR_TURN" message to the current player
            string turnMsg = "YOUR_TURN";
            SendMessageToClient(turnMsg, clientConnectionID, TransportPipeline.ReliableAndInOrder);
        }
    }

    public void SendMessageToClient(string msg, int connectionID, TransportPipeline pipeline)
    {
        NetworkPipeline networkPipeline = reliableAndInOrderPipeline;
        if (pipeline == TransportPipeline.FireAndForget)
            networkPipeline = nonReliableNotInOrderedPipeline;

        byte[] msgAsByteArray = Encoding.Unicode.GetBytes(msg);
        NativeArray<byte> buffer = new NativeArray<byte>(msgAsByteArray, Allocator.Persistent);
        DataStreamWriter streamWriter;

        networkDriver.BeginSend(networkPipeline, idToConnectionLookup[connectionID], out streamWriter);
        streamWriter.WriteInt(buffer.Length);
        streamWriter.WriteBytes(buffer);
        networkDriver.EndSend(streamWriter);

        buffer.Dispose();
    }

    //public void SendMessageToClient(string msg, NetworkConnection networkConnection)
    //{
    //    byte[] msgAsByteArray = Encoding.Unicode.GetBytes(msg);
    //    NativeArray<byte> buffer = new NativeArray<byte>(msgAsByteArray, Allocator.Persistent);


    //    //Driver.BeginSend(m_Connection, out var writer);
    //    DataStreamWriter streamWriter;
    //    //networkConnection.
    //    networkDriver.BeginSend(reliableAndInOrderPipeline, networkConnection, out streamWriter);
    //    streamWriter.WriteInt(buffer.Length);
    //    streamWriter.WriteBytes(buffer);
    //    networkDriver.EndSend(streamWriter);

    //    buffer.Dispose();
    //}

}
public enum TransportPipeline
{
    NotIdentified,
    ReliableAndInOrder,
    FireAndForget
}
