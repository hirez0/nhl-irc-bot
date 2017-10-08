using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;

namespace NHLScoreBot.UserInteraction
{
    class Text : IIRC
    {
        private BackgroundWorker _bw;
        private Queue<string> _commandQueue;

        public Text()
        {
            _commandQueue = new Queue<string>();
            _bw = new BackgroundWorker();
            _bw.DoWork += delegate(object s, DoWorkEventArgs args)
                {
                    while (true)
                    {
                        string cmd = Console.ReadLine();
                        QueueCommand(cmd);
                    }
                };
            _bw.RunWorkerAsync();
        }

        private void QueueCommand(string cmd)
        {
            lock (_commandQueue)
            {
                _commandQueue.Enqueue(cmd);
            }
        }

        private string DequeueCommand()
        {
            lock (_commandQueue)
            {
                if (_commandQueue.Count > 0)
                    return _commandQueue.Dequeue();
            }
            return null;
        }

        public void JoinIRC()
        {
            // nuttin
        }

        public void SendMessage(string msg, Command cmd)
        {
            Console.WriteLine(msg);
        }

        public Command GetCommand()
        {
            Command c = null;
            string cmd = DequeueCommand();
            if (cmd != null)
            {
                c = new Command("Text", "Text", cmd, false);
                Console.WriteLine("got command: " + cmd);
            }
            return c;
        }

        public void SetRunning(bool running)
        {
            // nuttin
        }

        public bool GetRunning()
        {
            return true;
        }

        public void ChangeNick(string nick)
        {
            // nuttin
        }

        public void Kick(string nick)
        {
            // nuttin
        }
    }
}
