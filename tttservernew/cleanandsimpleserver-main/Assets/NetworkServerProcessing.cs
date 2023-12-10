using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using System.Text;
using System;
using UnityEditor.Experimental.GraphView;
using Unity.VisualScripting;
using System.Security.Cryptography;

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

static public class NetworkServerProcessing
{
    const char sep = ',';
    #region Send and Receive Data Functions
    static public void ReceivedMessageFromClient(string msg, int clientConnectionID, TransportPipeline pipeline)
    {
        LoadData();
        Debug.Log("Network msg received =  " + msg + ", from connection id = " + clientConnectionID + ", from pipeline = " + pipeline);

        string[] csv = msg.Split(sep);
        int signifier = int.Parse(csv[0]);

        if (signifier == ClientToServerSignifiers.ChatMSG)
        {
            if (csv.Length < 3)
            {
                Debug.Log("From Server: ERROR... Invalid message format.");
                return;
            }
            string chatusername = csv[1];
            string chattext = csv[2];

            if (chatusername == "")    //if username is empty chat default username will be depression:
            {
                chatusername = "yeahdawg101";
                Debug.Log("From Server: Username cannot be blank.");
            }
            for (int i = 0; i < networkServer.networkConnections.Length; i++)   //send to all connected users
            {
                //1,username,text
                SendMessageToClient(ServerToClientSignifiers.ChatMSG.ToString() + sep + chatusername + sep + chattext, i, TransportPipeline.ReliableAndInOrder);
            }
        }
        else if (signifier == ClientToServerSignifiers.MakeAccount)
        {
            // Check if an account with the same username already exists
            foreach (Account account in Data.accounts)
            {
                if (account.username == csv[1])
                {
                    SendMessageToClient(ServerToClientSignifiers.Debug.ToString() + sep +
                        "Account already exists", clientConnectionID, TransportPipeline.ReliableAndInOrder);
                    return;
                }
            }
            //hash the Account password
            string hashedPassword = Convert.ToBase64String(SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(csv[2])));
            Account newAccount = new Account(csv[1], hashedPassword);
            // Add the new account to the accounts list
            Data.accounts.AddLast(newAccount);
            // Save the updated accounts list to disk
            SaveData();
            SendMessageToClient(ServerToClientSignifiers.Debug.ToString() + sep +
                "Account created successfully", clientConnectionID, TransportPipeline.ReliableAndInOrder);
        }
        else if (signifier == ClientToServerSignifiers.LoginData)
        {
            //unhash the password
            string hashedPassword = Convert.ToBase64String(SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(csv[2])));
            // Check if an account with the same username and password exists
            foreach (Account account in Data.accounts)
            {
                if (account.username == csv[1] && account.password == hashedPassword)
                {
                    // If it does, send a success message back to the client and return
                    string usernameMsg = csv[1];
                    string logindatamsg = ServerToClientSignifiers.LoginData.ToString() + sep + usernameMsg;

                    SendMessageToClient(logindatamsg, clientConnectionID, TransportPipeline.ReliableAndInOrder);
                    SendMessageToClient(ServerToClientSignifiers.Debug.ToString() + sep +
                    "Welcome " + usernameMsg, clientConnectionID, TransportPipeline.ReliableAndInOrder);
                    return;
                }
            }
            SendMessageToClient(ServerToClientSignifiers.Debug.ToString() + sep +
                "Invalid username or password", clientConnectionID, TransportPipeline.ReliableAndInOrder);
        }

    }
    static public void SendMessageToClient(string msg, int clientConnectionID, TransportPipeline pipeline)
    {
        networkServer.SendMessageToClient(msg, clientConnectionID, pipeline);
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

    #region Account data stuff

    static public void SaveData()
    {
        Debug.Log(Application.persistentDataPath.ToString() + Path.DirectorySeparatorChar + "datafile.txt");
        LinkedList<string> saveData = SerializeAccountData();

        StreamWriter writer = new StreamWriter(Application.persistentDataPath.ToString() + Path.DirectorySeparatorChar + "datafile.txt");
        foreach (string line in saveData)
            writer.WriteLine(line);
        writer.Close();
    }

    static public void LoadData()
    {
        Data.accounts.Clear();
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
            serializedData.AddLast(account.username + sep + account.password);
        }
        return serializedData;
    }

    static private void DeserializeSaveData(LinkedList<string> serializedData)
    {
        Account account = null;

        foreach (string line in serializedData)
        {
            if (line.Contains(sep))
            {
                string[] csv = line.Split(sep);
                account = new Account(csv[0], csv[1]);
                Data.accounts.AddLast(account);
            }
            else
            {
                Debug.Log("Invalid line in data file: " + line);
            }
        }
    }
    #endregion

    #region Setup
    static NetworkServer networkServer;
    static GameLogic gameLogic;

    static public void SetNetworkServer(NetworkServer NetworkServer)
    {
        networkServer = NetworkServer;
    }
    static public NetworkServer GetNetworkServer()
    {
        return networkServer;
    }
    static public void SetGameLogic(GameLogic GameLogic)
    {
        gameLogic = GameLogic;
    }

    #endregion
}

#region Protocol Signifiers
static public class ClientToServerSignifiers
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
    public const int Debug = 69;
}

#endregion

