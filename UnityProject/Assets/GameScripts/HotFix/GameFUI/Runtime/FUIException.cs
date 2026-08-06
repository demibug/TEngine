using System;
using TEngine;

namespace GameFUI
{
    /// <summary>
    /// FairyGUI 模块异常。
    /// </summary>
    [Serializable]
    public class FUIException : GameFrameworkException
    {
        public FUIException()
        {
        }

        public FUIException(string message)
            : base(message)
        {
        }

        public FUIException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
