#!/bin/bash
# usage: run.sh name 'json-steps'  -> waits until done, prints console log
n=$1; echo "$2" > /tmp/cmd/$n.json
for i in $(seq 1 600); do [ -f /tmp/cmd/$n.done ] && { cat /tmp/cmd/$n.done; exit 0; }; sleep 0.5; done; echo timeout
