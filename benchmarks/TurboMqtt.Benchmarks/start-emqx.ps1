docker run -d --name emqx-bench -p 1883:1883 -p 18083:18083 `
-e EMQX_MQTT__MAX_MQUEUE_LEN=100000 `
-e EMQX_FORCE_SHUTDOWN__ENABLE=false `
-e EMQX_MQTT__MAX_INFLIGHT=1000 `
emqx/emqx