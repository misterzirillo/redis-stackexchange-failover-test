#r "nuget: StackExchange.Redis"

open StackExchange.Redis

// matching endpoints defined in docker compose file
let sentinelEndpoints =
    [ "localhost", 26379
      "localhost", 26380
      "localhost", 26381 ]

// matching sentinel service name
let serviceName = "myredis"

// https://stackexchange.github.io/StackExchange.Redis/ThreadTheft
ConnectionMultiplexer.SetFeatureFlag("preventthreadtheft", true)

let connectSentinel () =
    async {
        let sentinelConfig = new ConfigurationOptions()
        sentinelConfig.ServiceName <- serviceName
        sentinelConfig.AbortOnConnectFail <- false

        for host, port in sentinelEndpoints do
            sentinelConfig.EndPoints.Add(host, port)

        let! connection =
            ConnectionMultiplexer.SentinelConnectAsync(sentinelConfig)
            |> Async.AwaitTask

        return connection
    }

let connectMaster (sentinelMultiplexer: ConnectionMultiplexer) =
    let config = new ConfigurationOptions()
    config.ServiceName <- serviceName
    sentinelMultiplexer.GetSentinelMasterConnection(config)

let doWithRetries (maxTries: uint) asyncOperation : Async<Result<'a, string>> =
    let rec repeatAsyncOperation retries =
        async {
            if retries <= maxTries then
                try
                    let! it = asyncOperation
                    return Ok it
                with
                | ex ->
                    printfn "An exception occurred on try %d" retries
                    printfn "%A" ex
                    return! repeatAsyncOperation (retries + 1u)
            else
                return Error "Maximum retries reached"
        }

    repeatAsyncOperation 0u


// get connection, generate a bunch of traffic
async {
    use! sentinelConnection = connectSentinel ()
    use masterConnection = connectMaster sentinelConnection

    let incrementKey =
        async {
            let db = masterConnection.GetDatabase()

            return!
                db.StringIncrementAsync(RedisKey "key")
                |> Async.AwaitTask
        }

    let mutable loop = true

    while loop do
        match! doWithRetries 5u incrementKey with
        | Ok newCount ->
            printfn "Count: %d" newCount

            for endpoint in masterConnection.GetEndPoints() do
                let server = masterConnection.GetServer endpoint

                let isReplica =
                    if server.IsReplica then
                        "replica"
                    else
                        "master"

                printfn "Endpoint %A : %s" endpoint isReplica

            do! Async.Sleep 5000
        | Error _ ->
            printfn "Retries exceeded; terminating"
            loop <- false
}
|> Async.RunSynchronously
