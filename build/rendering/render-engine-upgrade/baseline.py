"""Diagnostic acquisition and evidence-bound baseline controls; overall reviewed handoff is separate."""
import argparse
from datetime import datetime,timezone
from pathlib import Path
import sys
from baseline_contracts import (ContractError,analyze_owner,freeze_plan,native_identity,qualified_wave0_snapshot,read_json,sha256_bytes,write_new)
from provider import acquire

def main(argv=None):
    parser=argparse.ArgumentParser(description=__doc__);sub=parser.add_subparsers(dest='command',required=True)
    p=sub.add_parser('plan');p.add_argument('--execution-source',required=True);p.add_argument('--planning-source',required=True);p.add_argument('--output',required=True)
    p=sub.add_parser('assemble-proposal');p.add_argument('--draft',required=True);p.add_argument('--repository',required=True);p.add_argument('--output',required=True)
    p=sub.add_parser('validate-proposal');p.add_argument('--input',required=True);p.add_argument('--repository',required=True);p.add_argument('--output',required=True)
    p=sub.add_parser('analyze-owner');p.add_argument('--input',required=True);p.add_argument('--output',required=True);p.add_argument('--historical',action='store_true')
    p=sub.add_parser('diagnose');p.add_argument('--output',required=True);p.add_argument('--nvidia-smi',required=True);p.add_argument('--manifest');p.add_argument('--global-json');p.add_argument('--uuid');p.add_argument('--pid',type=int);p.add_argument('--hwnd',type=lambda v:int(v,0));p.add_argument('--samples',type=int,default=3);p.add_argument('--interval',type=float,default=1)
    p=sub.add_parser('admit');p.add_argument('--output',required=True);p.add_argument('--cohort');p.add_argument('--repository');p.add_argument('--trusted-master-source')
    args=parser.parse_args(argv)
    try:
        if args.command=='plan':
            value=freeze_plan(args.execution_source,args.planning_source,datetime.now(timezone.utc));write_new(args.output,value)
        elif args.command=='assemble-proposal':
            from baseline_proposals import assemble
            try:
                path=Path(args.draft).resolve();write_new(args.output,assemble(read_json(path),path.parent,args.repository))
            except (OSError,ValueError,KeyError,TypeError,IndexError) as error:
                write_new(args.output,dict(schemaVersion=1,kind='baseline-proposal-attempt',status='failed',accepted=False,qualified=False,operation=args.command,reason=str(error)))
                raise ContractError(str(error)) from error
        elif args.command=='validate-proposal':
            from baseline_proposals import validate_bound_proposal
            try:
                path=Path(args.input).resolve();write_new(args.output,validate_bound_proposal(read_json(path),path.parent,args.repository))
            except (OSError,ValueError,KeyError,TypeError,IndexError) as error:
                write_new(args.output,dict(schemaVersion=1,kind='baseline-proposal-attempt',status='failed',accepted=False,qualified=False,operation=args.command,reason=str(error)))
                raise ContractError(str(error)) from error
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
            acquire(args.output,args.nvidia_smi,native,args.uuid,args.samples,args.interval,args.pid,args.hwnd)
        else:
            from baseline_admission import evaluate
            try:
                if not all((args.cohort,args.repository,args.trusted_master_source)):
                    raise ContractError('Admission requires a hash-bound actual cohort, repository, and explicit accepted-master source; diagnostic labels alone cannot qualify')
                cohort_path=Path(args.cohort).resolve()
                value=evaluate(read_json(cohort_path),cohort_path.parent,args.repository,args.trusted_master_source)
            except (OSError,ValueError,KeyError,TypeError,IndexError) as error:
                write_new(args.output,dict(schemaVersion=1,kind='baseline-admission',status='blocked',physicalAcceptance='open',wave2Ready=False,reasons=[str(error)]))
                raise ContractError(str(error)) from error
            write_new(args.output,value)
        return 0
    except (OSError,ValueError,KeyError) as error:
        print(str(error),file=sys.stderr);return 2
if __name__=='__main__':raise SystemExit(main())
