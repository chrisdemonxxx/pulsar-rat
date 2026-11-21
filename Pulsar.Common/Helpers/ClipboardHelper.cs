using System;
using System.Windows.Forms;

namespace Pulsar.Common.Helpers
{
    public static class ClipboardHelper
    {
        public static bool SetClipboardTextSafe(string text)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
