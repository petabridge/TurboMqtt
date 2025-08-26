TurboMqtt aims to be a high-performance MQTT library, but it has a number of known stability problems with the MQTT 3.1.1 implementation:

1. Inconsistent handling of connection retries
2. Decoding problems
3. Performance issues, possibly
4. Numerous failing PRs on the main repository: https://github.com/petabridge/TurboMqtt

You are working locally off of my fork of this repository. 

Your goals:

- [ ] Upgrade to the latest version of Akka.NET
- [ ] Find any racy test failures (see if there are any on the open pull requests)
- [ ] Find issues with the MQTT decoding pipeline and simplify
- [ ] See if we can simplify the MQTT transport pipeline and improve network connection handling
- [ ] Maximize performance on the MQTT 3.1.1 transmission pipeline

* Add any new issues or bugs you encounter to this file.
* Mark off any completed issues to this file. 
* Never commit this file (`prompt.md`) to the repository. 
* Each time you successfully complete a change, commit it without gpg signing but never push anything to any remotes.

You may have to run the test suites many times in order to reproduce racy test failures locally - aggressively look for and fix those. 

Look at expanding our use of FsCheck / other property-based testing libraries in .NET to drum out error sources from our encoding / decoding pipelines.

If you suspect there are Akka.NET bugs or performance issues caused by it - save those as memories to memorizer.