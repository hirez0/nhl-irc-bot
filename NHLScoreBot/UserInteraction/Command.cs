using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NHLScoreBot.UserInteraction
{
    class Command
    {
        private string channel;
        private string userName;
        private string command;
        bool privateMessage;
        private bool opLevel;
        private string rawText;

        public string Channel
        {
            get { return channel; }
            set { channel = value; }
        }

        public bool PrivateMessage
        {
            get { return privateMessage; }
            set { privateMessage = value; }
        }

        public string UserName
        {
            get { return userName; }
            set { userName = value; }
        }

        public Command(string _channel, string _userName, string _rawText, bool _privateMessage)
        {
            command = string.Empty;

            channel = _channel;
            userName = _userName;
            rawText = _rawText;

            privateMessage = _privateMessage;

            SetCommand();
        }

        private void SetCommand()
        {
            if (rawText.Length > 1 && rawText[0] == '!')
            {
                if (HasArgument())
                    command = rawText.Substring(1, rawText.IndexOf(' ') - 1);
                else
                    command = rawText.Substring(1);
            }
        }

        public string GetArgument()
        {
            String result = String.Empty;
            int i = rawText.IndexOf(' ');
            if (i >= 2)
                result = rawText.Substring(i + 1).ToLower();

            return result;
        }

        public string GetArgumentOriginalCase()
        {
            String result = String.Empty;
            int i = rawText.IndexOf(' ');
            if (i >= 2)
                result = rawText.Substring(i + 1);

            return result;
        }

        public bool HasArgument()
        {
            return GetArgument().Length > 0;
        }

        public bool Matches(string s)
        {
            return command.ToLower().Trim().CompareTo(s.Substring(1).ToLower()) == 0;
        }
    }
}
