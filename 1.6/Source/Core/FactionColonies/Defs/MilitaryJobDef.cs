using System;
using Verse;

namespace FactionColonies
{
    public class MilitaryJobDef : Def
    {
        public Type handlerClass;
        public string statusLabelKey;
        public bool occupiesTarget = true;
        public bool isState;
        public FCStatDef cooldownStatDef;
        public bool deadPawnCooldown;
        public string floatMenuLabelKey;
        public string floatMenuDescKey;
        public string rewardsDesc;
        public bool defaultEnabled = true;

        [Unsaved] private MilitaryJobHandler cachedHandler;

        public MilitaryJobHandler Handler
        {
            get
            {
                if (cachedHandler == null && handlerClass != null)
                {
                    cachedHandler = (MilitaryJobHandler)Activator.CreateInstance(handlerClass);
                    cachedHandler.def = this;
                }
                return cachedHandler;
            }
        }
    }
}
