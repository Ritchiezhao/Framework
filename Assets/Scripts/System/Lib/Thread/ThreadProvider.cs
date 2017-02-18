﻿using CatLib.Contracts.Thread;

namespace CatLib.Thread
{

    public class ThreadProvider : ServiceProvider
    {

        public override void Register()
        {
            App.Singleton<MainThreadDispatcher>().Alias<IMainThreadDispatcher>();
        }
    }

}