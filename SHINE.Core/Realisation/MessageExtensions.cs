using System;
using System.Collections.Generic;
using System.Text;

namespace SHINE.Core
{
    public static class MessageExtensions
    {
        public static IMessage Deserialize(this byte[] data)
        {
            var msg = new Message();
            msg.Deserialize(data);
            return msg;
        }


    }
}
