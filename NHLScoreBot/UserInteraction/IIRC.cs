using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NHLScoreBot.UserInteraction
{
    interface IIRC
    {
        void JoinIRC();
        void SendMessage(string msg, Command cmd);
        Command GetCommand();
        void SetRunning(bool running);
        bool GetRunning();
        void ChangeNick(string nick);
        void Kick(string nick);
    }
}
