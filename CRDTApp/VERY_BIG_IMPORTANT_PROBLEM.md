- how do heartbeat mechanisms map to connection interruption events?
	- If the hearbeat is supposed to detect interruptions in connection, then it must perform some kind of temporal input tracking, or else it isn't doing anything that other layers wouldn't be able to do.
	- If the heartbeat isn't supposed to track receptions, then we are assuming we can detect connection interruptions from the send operation alone

	but isn't the goal to do it from lack of communication?

	possible solution: heartbeat mechanisms aren't responsible for any event calling. they only send messages. Instead, there's a "track time since last received message"-layer that uses that to (optionally) establish whether a connection is active. this tracker layer would live very near the bottom of the stack, below any possible heartbeat


	the usual stack should be:


	anything else > ack >  protocol + heartbeat > track connections > reception tracker > base