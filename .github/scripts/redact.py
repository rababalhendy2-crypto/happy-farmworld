"""Copy stdin to stdout with Unity account credentials replaced by ***.
Unity prints its command line (which carries -username/-password) into the log, so every
line is filtered before it is written anywhere, including the saved CI log."""
import os
import sys

secrets = [v for v in (os.environ.get("UNITY_PASSWORD"), os.environ.get("UNITY_EMAIL")) if v and len(v) >= 3]
for line in sys.stdin:
    for s in secrets:
        line = line.replace(s, "***")
    if "-password" in line:
        parts = line.split("-password")
        line = parts[0] + "-password ***\n"
    sys.stdout.write(line)
    sys.stdout.flush()
