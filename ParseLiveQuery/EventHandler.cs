using Parse;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace YB.Parse.LiveQuery;

public static class EventHandler
{

    public delegate void LiveQueryUpdateHandler<T>(T obj, T original) where T : ParseObject;
    public delegate void LiveQueryGeneralHandler<T>(T obj) where T : ParseObject;
}