#r "nuget: StackExchange.Redis"

open StackExchange.Redis

let sentinelEndpoints =
    [ "localhost", 26379
      "localhost", 26380
      "localhost", 26381 ]

let serviceName = "myredis"

// https://stackexchange.github.io/StackExchange.Redis/ThreadTheft
ConnectionMultiplexer.SetFeatureFlag("preventthreadtheft", true)

let connectSentinel () =
    async {
        let sentinelConfig = new ConfigurationOptions()

        for host, port in sentinelEndpoints do
            sentinelConfig.EndPoints.Add(host, port)

        sentinelConfig.ServiceName <- serviceName
        sentinelConfig.AbortOnConnectFail <- false

        let! connection =
            ConnectionMultiplexer.SentinelConnectAsync(sentinelConfig)
            |> Async.AwaitTask

        return connection
    }

let connectMaster (sentinelMultiplexer: ConnectionMultiplexer) =
    let config = new ConfigurationOptions()
    config.ServiceName <- serviceName

    let connection =
        sentinelMultiplexer.GetSentinelMasterConnection(config)

    connection


let doWithRetries (maxTries: int) asyncOperation : Async<Result<'a, string>> =
    let rec repeatAsyncOperation retries =
        async {
            if retries < maxTries then
                try
                    let! it = asyncOperation
                    return Ok it
                with
                | ex ->
                    printfn "An exception occurred on retry %d" retries
                    printfn "%A" ex
                    return! repeatAsyncOperation (retries + 1)
            else
                return Error "Maximum retries reached"

        }

    repeatAsyncOperation 0


// get connection, generate a bunch of traffic
async {
    use! sentinelConnection = connectSentinel ()
    use masterConnection = connectMaster sentinelConnection


    // graceful shutdown hook
    let mutable kill = false
    System.AppDomain.CurrentDomain.ProcessExit.Add(fun _ -> kill <- true)

    while not kill do
        let incrementKey =
            async {
                let db = masterConnection.GetDatabase()

                return!
                    db.StringIncrementAsync(RedisKey "key")
                    |> Async.AwaitTask
            }

        let! result = doWithRetries 5 incrementKey

        match result with
        | Ok newCount ->
            printfn "Count: %d" newCount

            for endpoint in masterConnection.GetEndPoints() do
                let server = masterConnection.GetServer endpoint
                let isReplica = server.IsReplica

                printfn
                    "endpoint %A : %s"
                    endpoint
                    (if isReplica then
                         "replica"
                     else
                         "master")

            do! Async.Sleep 5000
        | Error e ->
            printfn "Terminating: %s" e
            kill <- true

    return ()
}
|> Async.RunSynchronously
