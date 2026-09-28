Maybe the distance-vector routing algorithm just need to update on certain occasions? rather than constantly sending the table as an heartbeat, which would lead to a lot of exchanged data if there were a lot of replicas.... I could have a request mechanism...

- PROBLEMS EVERYWHERE! FLOODING DOESN'T WORK. MANY TIMEOUTS HERE AND IN THE SIMULATION DON'T WORK!

- THERE'S NO WAY TO TURN OFF THE NETWORK SIMULATORS & BEHAVIOURS! THEIR TASKS WILL RUN FOREVER!!!!

- HOW DO I TRACK THAT ALL MESSAGES ARE DONE, even between other replicas????????????????????

"Solution"?:
Maybe add an TaskEnded Event that gets called when the behaviour itself finishes?
(and can be used to know the end time of the simulaton, while letting the network thing run in the background) 
Close gets called externally and propagates the close() to the behaviour and network levels (which must have ways of turning off)

I MUST GIVE EVERYTHING A WAY TO TURN OFF!!!!
