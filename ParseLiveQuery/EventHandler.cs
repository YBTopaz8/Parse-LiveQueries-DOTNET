using Parse;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Parse.LiveQuery;

public static class ParseEventHandler
{

    public delegate void LiveQueryUpdateHandler<T>(T obj, T original) where T : ParseObject;
    public delegate void LiveQueryGeneralHandler<T>(T obj) where T : ParseObject;
}