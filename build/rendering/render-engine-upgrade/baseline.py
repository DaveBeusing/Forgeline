"""Fail-closed wave 1 diagnostic entry point; no accepted benchmark admission."""
import argparse
from datetime import datetime,timezone
from pathlib import Path
import sys
from baseline_contracts import (ContractError,analyze_owner,freeze_plan,native_identity,qualified_wave0_snapshot,read_json,sha256_bytes,write_new)
from provider import acquire

def main(argv=None):
    parser=argparse.ArgumentParser(description=__doc__);sub=parser.add_subparsers(dest='command',required=True)
    p=sub.add_parser('plan');p.add_argument('--execution-source',required=True);p.add_argument('--planning-source',required=True);p.add_argument('--output',required=True)
    p=sub.add_parser('analyze-owner');p.add_argument('--input',required=True);p.add_argument('--output',required=True);p.add_argument('--historical',action='store_true')
    p=sub.add_parser('diagnose');p.add_argument('--output',required=True);p.add_argument('--nvidia-smi',required=True);p.add_argument('--manifest');p.add_argument('--global-json');p.add_argument('--uuid');p.add_argument('--samples',type=int,default=3);p.add_argument('--interval',type=float,default=1)
    p=sub.add_parser('admit');p.add_argument('--output',required=True)
    args=parser.parse_args(argv)
    try:
        if args.command=='plan':
            value=freeze_plan(args.execution_source,args.planning_source,datetime.now(timezone.utc));write_new(args.output,value)
        elif args.command=='analyze-owner':
            raw=Path(args.input).read_bytes()
            from baseline_contracts import parse_json_bytes
            value=analyze_owner(parse_json_bytes(raw));value.update(inputSha256=sha256_bytes(raw),inputPath=str(Path(args.input).resolve()),historical=args.historical)
            write_new(args.output,value)
        elif args.command=='diagnose':
            native=None
            if args.manifest:
                if not args.global_json:raise ContractError('Actual source global.json is required with a native manifest')
                native=native_identity(read_json(args.manifest),read_json(args.global_json)['sdk']['version'])
            acquire(args.output,args.nvidia_smi,native,args.uuid,args.samples,args.interval)
        else:
            value=dict(kind='baseline-admission',status='blocked',physicalAcceptance='open',wave2Ready=False,
                       reasons=['renderer-to-provider physical identity unavailable','previously reviewed hardware/allocation/GPU-memory policies unavailable',
                                'exact-source required CI admission unverified','native-fixture live binding unavailable','qualified A/A cohorts unavailable'])
            write_new(args.output,value);qualified_wave0_snapshot()
        return 0
    except (OSError,ValueError,KeyError) as error:
        print(str(error),file=sys.stderr);return 2
if __name__=='__main__':raise SystemExit(main())
