module LogProvider

open Quartz.Logging;
open System;

type ConsoleLogProvider() =
    interface ILogProvider with
        member _.GetLogger _ =
            let logFn (level: LogLevel) (func: Func<string>) _ _ =
                if level >= LogLevel.Info && not (isNull func) then
                    printf "[%s] [%s] %s\n" (DateTime.Now.ToLongTimeString()) (level.ToString()) (func.Invoke())
                true
            logFn

        member _.OpenNestedContext _ =
            raise (NotImplementedException ())

        member _.OpenMappedContext (_, _, _) =
            raise (NotImplementedException ())