using MessagingToolkit.QRCode.Codec;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace WireguardApp
{
    /// <summary>
    /// Логика взаимодействия для MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        bool endProcess = false, newDataInStream = true, commandGo = false, toConsole = true, onLoad = false;

        Task MainLoop = null;
        QRCodeEncoder QrEncoder = new QRCodeEncoder();

        Process process;
        StreamWriter ProcStreamWriter;
        string msg = "";
        string lastCommandResult = "";
        string nextCommand = "";

        Dictionary<string, string> DeSerializeData = new Dictionary<string, string>();

        string PathToConf = "WireguardManager.conf";
        string PathToSave = "";
        string CurUser = "";

        WGUser ServerData = new WGUser();
        List<WGUser> UserList = new List<WGUser>();

        string[] ValidCommands =
            {
                "App.ConnectToServer",
                "App.GetInWG",
                "App.GetUsers",
                "App.SetPrvPubK",
                "App.TakeUsersInList",
                "App.GetWg0Conf",
                "App.SetUsers",
                "App.AddUser",
                "App.ClearConsole",
                "App.CreateNewWg0",
                "App.ConnectUsers",
                "App.DisConnectUsers",
                "App.RestartServer",
                "App.ReMoveUsers",
                "App.GetUsersAccesses"
            };

        public MainWindow()
        {
            InitializeComponent();
            DeSerializeConf(PathToConf);
            process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    Arguments = "/k",
                    FileName = DeSerializeData["PathToCmd"],
                    WorkingDirectory = DeSerializeData["WorkDir"]
                },
                EnableRaisingEvents = true
            };
            SetAndStartProcess();
            PathToSave = DeSerializeData["PathToSave"];
            nextCommand = "App.ConnectToServer";
            QrEncoder.QRCodeVersion = 0;
        }

        public class WGUser
        {
            public WGUser() { Position = -1; Ip = ""; Name = ""; PubKey = ""; PrvKey = ""; }
            public WGUser(string name)
            { Name = name; Ip = ""; PubKey = ""; PrvKey = ""; Position = -1; }
            public WGUser(string ip, string name, string pubk, string prvk)
            { Ip = ip; Name = name; PubKey = ""; PrvKey = ""; SetPosition(); }
            public void SetPosition()
            {
                if (this.Ip != "")
                {
                    string strPos = this.Ip.Substring(this.Ip.LastIndexOf('.') + 1,
                              this.Ip.LastIndexOf('/') - this.Ip.LastIndexOf('.') - 1);
                    this.Position = Convert.ToInt32(strPos);
                }
                else this.Position = -1;
            }
            public bool isConected() => this.Ip != "";
            public int Position { get; set; }
            public string Ip { get; set; }
            public string Name { get; set; }
            public string PubKey { get; set; }
            public string PrvKey { get; set; }
        }
        public int TakePos()
        {
            var ExistedIp = UserList.Select(u => u.Position);
            for (int i = 1; i < 256; i++)
                if (i != ServerData.Position && !ExistedIp.Contains(i))
                    return i;
            return -1;
        }
        public string TakeIpFromPos(int pos)
        {
            string IpFromPos = DeSerializeData["StandartUserIp"].Substring(0, DeSerializeData["StandartUserIp"].LastIndexOf('.') + 1) +
                pos.ToString() + DeSerializeData["StandartUserIp"].Substring(DeSerializeData["StandartUserIp"].IndexOf('/'));
            return IpFromPos;
        }

        public void DeSerializeConf(string path)
        {
            var WireGuardAppConf = File.ReadAllLines(path).ToList();

            foreach (string line in WireGuardAppConf)
                if (line != "")
                    DeSerializeData.Add(line.Substring(0, line.IndexOf('=')).Trim(' ').Trim(),
                                        line.Substring(line.IndexOf('=')+1).Trim(' ').Trim());
        }

        public void BlockUI()
        {
            if (!onLoad)
            {
                var els = MainGrid.Children.Cast<Control>();
                foreach (var el in els)
                    if(el.Name != "ClearConsoleBTN" && el.Name != "ConnectToServerBTN") 
                        el.IsEnabled = false;
                onLoad = true;
            }
        }
        public void UnBlockUI()
        {
            if (onLoad)
            {
                var els = MainGrid.Children.Cast<Control>();
                foreach (var el in els) el.IsEnabled = true;
                onLoad = false;
            }
        }

        public void SetAndStartProcess()
        {
            UserInputTextBox.KeyDown += new KeyEventHandler((s, e) =>
            {
                if (e.Key == Key.Enter) UserInput(s, e);
            });
            process.OutputDataReceived += new DataReceivedEventHandler((s, e) =>
            {
                msg += "\n" + e.Data;
                newDataInStream = true;
            });
            process.ErrorDataReceived += new DataReceivedEventHandler((s, e) =>
            {
                msg += "\n" + e.Data;
                newDataInStream = true;
            });
            process.Exited += new EventHandler((s, e) => Environment.Exit(0));

            process.Start();

            if (ProcStreamWriter == null)
            {
                ProcStreamWriter = process.StandardInput;
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }

            ProcStreamWriter.NewLine = "\n";
            MainLoop = ReadStreamPeriodicallyAsync(
                TimeSpan.FromSeconds(Convert.ToDouble(DeSerializeData["INTERVAL"])));
        }
        public void ConnectToServer()
        {
            if (!commandGo)
            {
                toConsole = false;
                ProcStreamWriter.WriteLine("ssh " + DeSerializeData["serverUser"] + "@" + DeSerializeData["serverIp"]);
                ServerData.Ip = DeSerializeData["StandartServerIp"];
                ServerData.SetPosition();
                commandGo = true;
                endProcess = false;
            }
            else if (commandGo)
            {
                nextCommand = "App.GetInWG";
                commandGo = false;
            }
        }

        public async Task ReadStreamPeriodicallyAsync(
        TimeSpan interval)
        {
            while (!endProcess)
            {
                await Task.Delay(interval);
                if (MainTextBox != null)
                {
                    if (newDataInStream)
                    {
                        lastCommandResult = msg;
                        if (toConsole)
                            MainTextBox.Text += "\n" + msg;
                        MainTextBox.ScrollToEnd();
                        msg = "";
                        newDataInStream = false;
                    }
                }

                if (nextCommand != "") BlockUI();

                switch (nextCommand)
                {
                    case "App.ConnectToServer":
                        ConnectToServer();
                        break;
                    case "App.GetInWG":
                        GetInWG();
                        break;
                    case "App.GetUsers":
                        GetUsers(); 
                        break;
                    case "App.SetPrvPubK":
                        SetPrvPubK();
                        break;
                    case "App.TakeUsersInList":
                        TakeUsersInList();
                        break;
                    case "App.GetWg0Conf":
                        GetWg0Conf();
                        break;
                    case "App.SetUsers":
                        SetUsers();
                        break;
                    case "App.AddUser":
                        AddUser();
                        break;
                    case "App.CreateNewWg0":
                        CreateNewWg0();
                        break;
                    case "App.ConnectUsers":
                        ConnectUsers();
                        break;
                    case "App.DisConnectUsers":
                        DisConnectUsers();
                        break;
                    case "App.RestartServer":
                        RestartServer();
                        break;
                    case "App.ReMoveUsers":
                        ReMoveUsers();
                        break;
                    case "App.GetUsersAccesses":
                        GetUsersAccesses();
                        break;

                    case "":
                        UnBlockUI();
                        break;
                    case "App.ClearConsole":
                        MainTextBox.Text = ""; nextCommand = ""; commandGo = false; onLoad = true;
                        break;
                    default:
                        ProcStreamWriter.WriteLine(nextCommand);
                        nextCommand = "";
                        break;
                }
            }
        }

        public void GetInWG()
        {
            if (!commandGo)
            {
                ProcStreamWriter.WriteLine("cd /etc/wireguard/");
                commandGo = true;
            }
            else if (commandGo)
            {
                MainTextBox.Text += "\nConnecting to server " + DeSerializeData["serverIp"] + " ...";
                ProcStreamWriter.WriteLine();
                nextCommand = "App.GetUsers";
                commandGo = false;
            }
        }
        
        public void GetUsers()
        {
            if (!commandGo)
            {
                ProcStreamWriter.WriteLine("ls");
                lastCommandResult = "";
                commandGo = true;
            }
            if (commandGo && lastCommandResult.Contains("wg0.conf"))
            {
                nextCommand = "App.TakeUsersInList";
                commandGo = false;
            }
        }
        public void TakeUsersInList()
        {
            UserList.Clear();
            var UsersInStr = lastCommandResult.Split('\n');
            foreach (string user in UsersInStr)
            {
                int indexOf_ = user.IndexOf('_');
                if (indexOf_ != -1&&user!="")
                {
                    string newName = user.Substring(0, indexOf_);
                    if (!UserList.Select(i => i.Name).Contains(newName))
                        UserList.Add(new WGUser(newName));
                }
            }
            nextCommand = "App.SetPrvPubK";
            CurUser = UserList[0].Name;
        }
        public void SetPrvPubK()
        {
            if (CurUser == "")
                nextCommand = "App.GetWg0Conf";

            else
            {
                int UserIndex = -1;
                UserIndex = UserList.IndexOf(UserList.FirstOrDefault(i => i.Name == CurUser));

                if (!commandGo)
                {
                    if (ServerData.PubKey == "") ProcStreamWriter.WriteLine("cat publickey");
                    else if (ServerData.PrvKey == "") ProcStreamWriter.WriteLine("cat privatekey");
                    else
                    {
                        if (UserList[UserIndex].PubKey == "")
                            ProcStreamWriter.WriteLine("cat " + CurUser + "_PubK");
                        else
                            ProcStreamWriter.WriteLine("cat " + CurUser + "_PrvK");
                    }
                    commandGo = true;
                    lastCommandResult = "";
                }

                if (commandGo && lastCommandResult != "")
                {
                    if (ServerData.PubKey == "") ServerData.PubKey = lastCommandResult.Substring(1);
                    else if (ServerData.PrvKey == "") ServerData.PrvKey = lastCommandResult.Substring(1);
                    else
                    {
                        if (UserList[UserIndex].PubKey == "")
                            UserList[UserIndex].PubKey = lastCommandResult.Substring(1);
                        else
                        {
                            UserList[UserIndex].PrvKey = lastCommandResult.Substring(1);
                            if (UserList.Count() > UserIndex + 1)
                                CurUser = UserList[UserIndex + 1].Name;
                            else CurUser = "";
                        }
                    }

                    commandGo = false;
                }
            }
        }
        public void GetWg0Conf()
        {
            if (!commandGo)
            {
                ProcStreamWriter.WriteLine("cat wg0.conf");
                commandGo = true;
                lastCommandResult = "";
            }
            if (commandGo && lastCommandResult != "")
            {
                nextCommand = "App.SetUsers";
                commandGo = false;
            }
        }
        public void SetUsers()
        {
            string Wg0 = lastCommandResult;
            List<string> partWg0 = new List<string>();
            Dictionary<string, string> IpFromPubK = new Dictionary<string, string>();

            while (lastCommandResult != "")
            {
                partWg0.Add("");
                int ketIndex = lastCommandResult.IndexOf(']');
                partWg0[partWg0.Count() - 1] += lastCommandResult.Substring(0, ketIndex + 1);
                lastCommandResult = lastCommandResult.Substring(ketIndex + 1);

                int braIndex = lastCommandResult.IndexOf('[');
                if (braIndex == -1) braIndex = lastCommandResult.Length + 1;
                partWg0[partWg0.Count() - 1] += lastCommandResult.Substring(0, braIndex - 1);
                lastCommandResult = lastCommandResult.Substring(braIndex - 1);

                string strPubK = partWg0.Last().Substring(
                    partWg0.Last().IndexOf('=') + 2);
                string strIp = strPubK.Substring(
                    strPubK.IndexOf('=') + 2);

                strPubK = strPubK.Substring(0, strPubK.IndexOf('=') + 1);
                strIp = strIp.Substring(strIp.IndexOf('=') + 2);
                strIp = strIp.IndexOf('\n') == -1 ? strIp : strIp.Substring(0, strIp.IndexOf('\n'));

                IpFromPubK.Add(strPubK, strIp);
            }

            ConnectedUsersListBox.Items.Clear();
            DisConnectedUsersListBox.Items.Clear();

            foreach (var Ips in IpFromPubK)
                foreach (var user in UserList)
                    if (user.PubKey == Ips.Key)
                    {
                        UserList[UserList.IndexOf(user)].Ip = Ips.Value;
                        UserList[UserList.IndexOf(user)].SetPosition();
                        ConnectedUsersListBox.Items.Add(UserList[UserList.IndexOf(user)].Name);
                    }
            foreach (var user in UserList) 
                if (user.Ip == "") DisConnectedUsersListBox.Items.Add(user.Name);
            UserList = UserList.OrderBy(i => i.Position).ToList();

            MainTextBox.Text += "\nWireGuard server " + DeSerializeData["serverIp"] + " connected";
            MainTextBox.ScrollToEnd();
            nextCommand = "";
            CurUser = "";
            commandGo = false;
            toConsole = true;
        }
        public void AddUser()
        {
            string newUserName = "";
            if (NewUserTextBox.Text != "") newUserName = NewUserTextBox.Text;

            if (!commandGo)
            {
                toConsole = false;
                ProcStreamWriter.WriteLine("wg genkey | tee /etc/wireguard/" + newUserName
                + "_PrvK | wg pubkey | tee /etc/wireguard/" + newUserName + "_PubK");
                commandGo = true;
                lastCommandResult = "";
            }
            if (commandGo && lastCommandResult != "")
            {
                MainTextBox.Text += "\n\n *New user: " + newUserName + " has been addied\n\n";
                NewUserTextBox.Text = "";
                nextCommand = "App.GetUsers";
                commandGo = false;
                toConsole = true;
            }
        }
        public void CreateNewWg0()
        {
            if (!commandGo)
            {
                ProcStreamWriter.WriteLine("rm wg0.conf");

                string NewWg0Conf = "";

                NewWg0Conf += "[Interface]\n";
                NewWg0Conf += "PrivateKey = " + ServerData.PrvKey + "\n";
                NewWg0Conf += "Address = " + DeSerializeData["StandartServerIp"] + "\n";
                NewWg0Conf += "ListenPort = " + DeSerializeData["StandartServerListenPort"] + "\n";
                NewWg0Conf += "PostUp = " + DeSerializeData["StandartServerPostUpArg"] + "\n";
                NewWg0Conf += "PostDown = " + DeSerializeData["StandartServerPostDownArg"] + "\n";

                foreach (var user in UserList)
                    if (user.Position != -1 && user.Position != 0 && user.Ip != "")
                    {
                        NewWg0Conf += "\n[Peer]\n";
                        NewWg0Conf += "PublicKey = " + user.PubKey + "\n";
                        NewWg0Conf += "AllowedIPs = " + user.Ip + "\n";
                    }

                ProcStreamWriter.WriteLine("echo \"" + NewWg0Conf + "\" > wg0.conf");
                commandGo = true;
            }
            if (commandGo)
            {
                nextCommand = "App.RestartServer";
                commandGo = false;
            }
        }
        public void ConnectUsers()
        {
            var UsersToConnectNames = DisConnectedUsersListBox.SelectedItems;
            var UsersToConnect = UserList.Where(i => UsersToConnectNames.Contains(i.Name)).ToList();
            foreach (var user in UsersToConnect)
            {
                UserList[UserList.IndexOf(user)].Position = TakePos();
                UserList[UserList.IndexOf(user)].Ip = TakeIpFromPos(UserList[UserList.IndexOf(user)].Position);
            }
            UserList = UserList.OrderBy(i => i.Position).ToList();
            nextCommand = "App.CreateNewWg0";
            commandGo = false;
        }
        public void DisConnectUsers()
        {
            var UsersToDisConnectNames = ConnectedUsersListBox.SelectedItems;
            var UsersToDisConnect = UserList.Where(i => UsersToDisConnectNames.Contains(i.Name)).ToList();
            foreach (var user in UsersToDisConnect)
            {
                UserList[UserList.IndexOf(user)].Ip = "";
                UserList[UserList.IndexOf(user)].Position = -1;
            }
            UserList = UserList.OrderBy(i => i.Position).ToList();
            nextCommand = "App.CreateNewWg0";
            commandGo = false;
        }
        public void RestartServer()
        {
            if (!commandGo)
            {
                ProcStreamWriter.WriteLine("systemctl restart wg-quick@wg0");
                ProcStreamWriter.WriteLine("systemctl status wg-quick@wg0");
                commandGo = true;
                lastCommandResult = "";
            }
            if (commandGo && lastCommandResult != "")
            {
                nextCommand = "App.ConnectToServer";
                commandGo = false;
            }
        }
        public void ReMoveUsers()
        {
            var UsersToReMoveNames = DisConnectedUsersListBox.SelectedItems;
            foreach (var user in UsersToReMoveNames)
            {
                ProcStreamWriter.WriteLine("rm " + user + "_PrvK");
                ProcStreamWriter.WriteLine("rm " + user + "_PubK");
            }
            nextCommand = "App.GetUsers";
            commandGo = false;
        }
        public void GetUsersAccesses()
        {
            var UsersToGetNames = ConnectedUsersListBox.SelectedItems;
            var UsersToGet = UserList.Where(i => UsersToGetNames.Contains(i.Name)).ToList();

            List<string> strUsersAccesses = new List<string>();
            foreach (var user in UsersToGet)
            {
                strUsersAccesses.Add("");
                strUsersAccesses[strUsersAccesses.Count() - 1] += "[Interface]\n";
                strUsersAccesses[strUsersAccesses.Count() - 1] += "PrivateKey = " + user.PrvKey + "\n";
                strUsersAccesses[strUsersAccesses.Count() - 1] += "Address = " + user.Ip + "\n";
                strUsersAccesses[strUsersAccesses.Count() - 1] += "DNS = " + DeSerializeData["StandartDNS"] + "\n\n";

                strUsersAccesses[strUsersAccesses.Count() - 1] += "[Peer]\n";
                strUsersAccesses[strUsersAccesses.Count() - 1] += "PublicKey = " + ServerData.PubKey + "\n";
                strUsersAccesses[strUsersAccesses.Count() - 1] += "AllowedIPs = " + DeSerializeData["StandartAllowedIPsServer"] + "\n";
                strUsersAccesses[strUsersAccesses.Count() - 1] += "EndPoint = " + DeSerializeData["serverIp"] + ":" + DeSerializeData["StandartServerListenPort"] + "\n";
                strUsersAccesses[strUsersAccesses.Count() - 1] += "PersistentKeepalive = " + DeSerializeData["serverKeepAlive"];

                MainTextBox.Text += "\n\n" + strUsersAccesses.Last();
                MainTextBox.ScrollToEnd();

                Directory.CreateDirectory(PathToSave + user.Name);
                Bitmap EncPic = new Bitmap(QrEncoder.Encode(strUsersAccesses.Last(), Encoding.UTF8));
                EncPic.Save(PathToSave + user.Name + "\\" + user.Name + ".jpg");
                var fileStream = File.Create(PathToSave + user.Name + "\\" + user.Name + ".conf");
                StreamWriter fileWriter = new StreamWriter(fileStream);
                fileWriter.WriteLine(strUsersAccesses.Last());
                fileWriter.Close();
                fileStream.Close();
            }

            nextCommand = "";
            commandGo = false;
        }

        private void Button_ConnectToServer(object sender, RoutedEventArgs e)
        {
            if (MainLoop.Status != TaskStatus.Running && MainLoop.Status != TaskStatus.WaitingForActivation)
                MainLoop = ReadStreamPeriodicallyAsync(
                    TimeSpan.FromSeconds(Convert.ToDouble(DeSerializeData["INTERVAL"])));

            nextCommand = "App.ConnectToServer";
            commandGo = false;
        }
        private void Button_RestartServer(object sender, RoutedEventArgs e)
        {
            commandGo = false;
            lastCommandResult = "";
            nextCommand = "App.RestartServer";
        }
        private void Button_GetUsersAccesses(object sender, RoutedEventArgs e)
        {
            nextCommand = "App.GetUsersAccesses";
        }
        private void Button_ClearConsole(object sender, RoutedEventArgs e)
        {
            nextCommand = "App.ClearConsole";
        }
        private void Button_ConnectUsers(object sender, RoutedEventArgs e)
        {
            if (DisConnectedUsersListBox.SelectedItems.Count > 0)
                nextCommand = "App.ConnectUsers";
        }
        private void Button_DisConnectUsers(object sender, RoutedEventArgs e)
        {
            if (ConnectedUsersListBox.SelectedItems.Count > 0)
                nextCommand = "App.DisConnectUsers";
        }
        private void Button_AddUser(object sender, RoutedEventArgs e)
        {
            if (NewUserTextBox.Text != "")
                nextCommand = "App.AddUser";
        }
        private void Button_ReMoveUsers(object sender, RoutedEventArgs e)
        {
            if (DisConnectedUsersListBox.SelectedItems.Count > 0)
                nextCommand = "App.ReMoveUsers";
        }
        private void UserInput(object sender, RoutedEventArgs e)
        {
            nextCommand = UserInputTextBox.Text;
            UserInputTextBox.Text = "";
        }
        public void MakeSSHAccess()
        {
            ProcStreamWriter.WriteLine("ssh-keygen");
            ProcStreamWriter.WriteLine();
            ProcStreamWriter.WriteLine();
            ProcStreamWriter.WriteLine();
            ProcStreamWriter.WriteLine();
            ProcStreamWriter.WriteLine(@"scp %UserProfile%\.ssh\id_rsa.pub" +
                DeSerializeData["serverUser"] + "@" + DeSerializeData["serverIp"] + ":/.ssh/authorized_keys");
        }
    }
}
