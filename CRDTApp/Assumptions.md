- I assume that INetworkManager ensures at-least-once delivery, 
RETURNING FALSE IF THE MESSAGE COULDN'T BE DELIVERED DUE TO CHANNEL INTERRUPTION/EXCESSIVE TIMEOUT
(i.e in this case, we assume the other node cannot be reached directly).