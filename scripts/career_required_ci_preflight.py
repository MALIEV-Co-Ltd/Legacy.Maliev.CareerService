"""Fresh physical-memory check in the existing required .NET job."""
import datetime as dt
import json
import os
from pathlib import Path
import re

PHYSICAL_FLOOR_KIB = 4194304


def observation(meminfo, environment):
    matches = re.findall(r'^MemFree:\s+([0-9]+)\s+kB$', meminfo, flags=re.MULTILINE)
    if len(matches) != 1:
        raise ValueError('exact physical MemFree observation required')
    keys = ('GITHUB_REPOSITORY', 'GITHUB_RUN_ID', 'GITHUB_RUN_ATTEMPT', 'GITHUB_JOB', 'GITHUB_SHA')
    if any(not isinstance(environment.get(k), str) or not environment[k] for k in keys):
        raise ValueError('actual required-CI job/run identity required')
    if not environment['GITHUB_RUN_ID'].isdecimal() or not environment['GITHUB_RUN_ATTEMPT'].isdecimal() or int(environment['GITHUB_RUN_ATTEMPT']) < 1 or not re.fullmatch('[0-9a-f]{40}', environment['GITHUB_SHA']):
        raise ValueError('canonical required-CI run/attempt/source identity required')
    free = int(matches[0])
    return {'checkedUtc': dt.datetime.now(dt.timezone.utc).isoformat(),
            'repository': environment['GITHUB_REPOSITORY'], 'runId': environment['GITHUB_RUN_ID'],
            'attempt': environment['GITHUB_RUN_ATTEMPT'], 'job': environment['GITHUB_JOB'],
            'sourceHead': environment['GITHUB_SHA'], 'freePhysicalKiB': free,
            'requiredPhysicalKiB': PHYSICAL_FLOOR_KIB, 'physicalFloorSatisfied': free >= PHYSICAL_FLOOR_KIB,
            'jobTimeoutMinutes': 30, 'runnerTeardown': 'standard GitHub-hosted job teardown',
            'nativeResultsProven': False}


def main():
    result = observation(Path('/proc/meminfo').read_text(encoding='ascii'), os.environ)
    print(json.dumps(result, sort_keys=True))
    return 0 if result['physicalFloorSatisfied'] else 1


if __name__ == '__main__':
    raise SystemExit(main())
