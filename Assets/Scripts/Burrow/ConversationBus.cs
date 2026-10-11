using System;

namespace Burrow
{
    // What the wearer said, as recognised text, for every open mind to react to.
    public static class ConversationBus
    {
        public static event Action<string> Utterance;
        public static void Publish(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            Utterance?.Invoke(text.Trim());
            if (TextWorld.Instance != null) TextWorld.Instance.Hear(text.Trim());
        }
    }
}
