using System.Collections;
using System.Collections.Generic;
using System.IO;
using Unity.Networking.Transport;
using UnityEngine;

static public class NetworkServerProcessing
{
    #region Send and Receive Data Functions
    const char sep = ',';

    static public void ReceivedMessageFromClient(string msg, int clientConnectionID, TransportPipeline pipeline)
    {
        //LoadData();
        Debug.Log("Network msg received =  " + msg + ", from connection id = " + clientConnectionID + ", from pipeline = " + pipeline);

        string[] csv = msg.Split(sep);
        int signifier = int.Parse(csv[0]);

        if (signifier == ClientToServerSignifiers.ChatMSG)
        {
            Debug.Log("got chat msg");
            if (csv.Length < 3)
            {
                Debug.Log("From Server: ERROR... Invalid message format.");
                return;
            }
            string chatusername = csv[1];
            string chattext = csv[2];

            if (string.IsNullOrWhiteSpace(chatusername))    //if username is empty chat default username will be depression:
            {
                chatusername = "yeahdawg";
                Debug.Log("From Server: Username cannot be blank.");
            }
            for (int i = 0; i < networkServer.networkConnections.Length; i++)   //send to all connected users
            {
                //1,username,text
                SendMessageToClient(ServerToClientSignifiers.ChatMSG.ToString() + sep + chatusername + sep + chattext, i, TransportPipeline.ReliableAndInOrder);
            }
        }
    }
    static public void SendMessageToClient(string msg, int clientConnectionID, TransportPipeline pipeline)
    {
        networkServer.SendMessageToClient(msg, clientConnectionID, pipeline);
    }

    static public void SaveData()
    {
        LinkedList<string> saveData = SerializeAccountData();

        StreamWriter writer = new StreamWriter(Application.persistentDataPath.ToString() + Path.DirectorySeparatorChar + "datafile.txt");
        foreach (string line in saveData)
            writer.WriteLine(line);
        writer.Close();
    }

    static public void LoadData()
    {
        //Data.accounts.Clear();
        LinkedList<string> serializedData = new LinkedList<string>();
        string line = "";
        StreamReader sr = new StreamReader(Application.persistentDataPath.ToString() + Path.DirectorySeparatorChar + "datafile.txt");
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
            serializedData.AddLast(networkServer.usernameSignifier.ToString() + sep + account.username + sep + account.password);
        }
        return serializedData;
    }

    static private void DeserializeSaveData(LinkedList<string> serializedData)
    {
        Account account = null;

        foreach (string line in serializedData)
        {
            string[] csv = line.Split(sep);
            int signifier = int.Parse(csv[0]);

            if (signifier == networkServer.usernameSignifier)
            {
                account = new Account(csv[1], csv[2]);
                Data.accounts.AddLast(account);
            }
        }
    }
    #endregion

    #region Connection Events

    static public void ConnectionEvent(int clientConnectionID)
    {
        Debug.Log("Client connection, ID == " + clientConnectionID);
    }
    static public void DisconnectionEvent(int clientConnectionID)
    {
        Debug.Log("Client disconnection, ID == " + clientConnectionID);
    }

    #endregion

    #region Setup
    static NetworkServer networkServer;

    static public void SetNetworkServer(NetworkServer NetworkServer)
    {
        networkServer = NetworkServer;
    }
    static public NetworkServer GetNetworkServer()
    {
        return networkServer;
    }


    #endregion
}

#region Protocol Signifiers
static public class ClientToServerSignifiers
{
    public const int ChatMSG = 1;
    public const int MakeAccount = 2;
    public const int LoginData = 3;
    public const int RoomJoin = 11;
    public const int RoomExit = 12;
    public const int Winner = 21;
    public const int Loser = 22;
}

static public class ServerToClientSignifiers
{
    public const int ChatMSG = 1;
    public const int MakeAccount = 2;
    public const int LoginData = 3;
    public const int CreateGame = 4;
    public const int WhosTurn = 5;
    public const int DisplayMove = 6;
    public const int Restart = 7;
    public const int RoomJoin = 11;
    public const int RoomExit = 12;
    public const int Winner = 21;
    public const int Loser = 22;
}

#endregion

