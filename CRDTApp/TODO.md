- Make sure everything is thread-safe/there isn't any kind of weird parallelism behaviour.
- Make sure all semaphore logic is correct

- I could create a distanceDetection layer that adds distances to the connection tracker, 
but the connection tracker would need a boolean to say if that layer is expected
and if that boolean is false, it would always return 1 (node connecnted) or -1 (node disconnected);
