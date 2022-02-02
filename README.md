# redis-stackexchange-failover-test

Testing Redis SE client library failover behavior.

## Usage

_Requires docker and dotnet CLI._

Start the Redis sentinel cluster and Redis nodes

`docker-compose up`

Begin the test script

`dotnet fsi FailoverTest.fsx`

Note the container ID of the master node and pause it

`docker ps` - copy container id

`docker container pause <container-id>`

Watch the test script for changes and errors
