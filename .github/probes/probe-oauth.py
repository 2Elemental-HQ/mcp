#!/usr/bin/env python3
"""Compare one unchanged OAuth assertion on immutable candidate/upstream sources; never publish packages."""
import hashlib, json, os, pathlib, platform, subprocess, sys, tempfile, time, xml.etree.ElementTree as ET
ROOT = pathlib.Path(__file__).resolve().parents[2]
OUT = pathlib.Path(sys.argv[1]).resolve()
OUT.mkdir(parents=True, exist_ok=False)
TEST = 'ModelContextProtocol.AspNetCore.Tests.OAuth.AuthTests.AuthorizationFlow_ConcurrentStepUps_ReuseSteppedUpToken_WhenChallengeAddsNoNewScope'
PROJECT = 'tests/ModelContextProtocol.AspNetCore.Tests/ModelContextProtocol.AspNetCore.Tests.csproj'
REFS = {'candidate':'c04230cd11f8e73986c426da8c7c4e043348d371','upstream':'3338e88e15c42cfc27465143c140d7d10f1a2707'}
rows=[]

def command(args,cwd,log,env=None):
    with (OUT/log).open('w') as output:
        result=subprocess.run(args,cwd=cwd,env=env,stdout=output,stderr=subprocess.STDOUT,timeout=600)
    return result.returncode

def save():
    (OUT/'summary.json').write_text(json.dumps({'test':TEST,'platform':platform.platform(),'refs':REFS,'rows':rows},indent=2)+'\n')

def test(repo,label,tfm,phase,delay=0,legacy=False,repeat=0):
    name=f'{label}-{tfm}-{phase}-{repeat}'
    env=os.environ.copy();env['MCP_PROBE_TOKEN_DELAY_MS']=str(delay);env['MCP_PROBE_LEGACY']='1' if legacy else '0'
    directory=OUT/name
    started=time.monotonic()
    rc=command(['dotnet','test',PROJECT,'-c','Release','-f',tfm,'--no-build','--filter',f'FullyQualifiedName={TEST}','--logger','trx;LogFileName=result.trx','--results-directory',str(directory)],repo,f'{name}.log',env)
    trx=directory/'result.trx'
    text=trx.read_text() if trx.exists() else ''
    tree=ET.fromstring(text) if text else None
    results=[n.attrib.get('outcome') for n in tree.iter() if n.tag.endswith('UnitTestResult')] if tree is not None else []
    expected_failure=delay>5000 and not legacy
    matched=(rc!=0 and results==['Failed'] and 'Assert.Single() Failure' in text and 'contained 2 items' in text) if expected_failure else (rc==0 and results==['Passed'])
    rows.append({'source':label,'framework':tfm,'phase':phase,'delayMilliseconds':delay,'legacyProtocol':legacy,'repeat':repeat,'exitCode':rc,'results':results,'expectedDuplicateAuthorizationFailure':expected_failure,'matched':matched,'seconds':time.monotonic()-started})
    save();print(name,rc,matched,flush=True)

command(['dotnet','--info'],ROOT,'dotnet-info.log')
for label,ref in REFS.items():
    repo=pathlib.Path(tempfile.mkdtemp(prefix=f'oauth-{label}-'))
    subprocess.run(['git','clone','--no-hardlinks','--no-checkout',str(ROOT),str(repo)],check=True)
    subprocess.run(['git','checkout','--detach',ref],cwd=repo,check=True)
    # Preserve the original global.json selection on the same runner image; setup-dotnet's installed
    # SDK is not necessarily the SDK selected by the repository's roll-forward policy.
    command(['dotnet','--info'],repo,f'{label}-dotnet-info.log')
    for tfm in ['net9.0','net10.0']:
        if command(['dotnet','build',PROJECT,'-c','Release','-f',tfm],repo,f'{label}-{tfm}-original-build.log'):
            raise SystemExit('Original build failed; inspect evidence')
        test(repo,label,tfm,'original')
    program=repo/'tests/ModelContextProtocol.TestOAuthServer/Program.cs'
    original=program.read_text()
    needle='''        // Token endpoint
        app.MapPost("/token", async (HttpContext context) =>
        {
'''
    injection='''        // Diagnostic fixture only: delay the first token exchange, preserving the production client timeout.
        var probeTokenRequests = 0;
        app.MapPost("/token", async (HttpContext context) =>
        {
            var probeSequence = Interlocked.Increment(ref probeTokenRequests);
            if (probeSequence == 1 && int.TryParse(Environment.GetEnvironmentVariable("MCP_PROBE_TOKEN_DELAY_MS"), out var probeDelay) && probeDelay > 0)
            {
                var probeWatch = System.Diagnostics.Stopwatch.StartNew();
                try { await Task.Delay(probeDelay, context.RequestAborted); }
                finally { Console.WriteLine($"OAUTH_PROBE first_token_ms={probeWatch.ElapsedMilliseconds} aborted={context.RequestAborted.IsCancellationRequested}"); }
            }
'''
    assert original.count(needle)==1
    program.write_text(original.replace(needle,injection))
    auth=repo/'tests/ModelContextProtocol.AspNetCore.Tests/OAuth/AuthTests.cs'
    content=auth.read_text();start=content.index('    public async Task '+TEST.split('.')[-1]+'()');end=content.index('\n    [Fact]',start)
    section=content[start:end]
    old='transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);'
    new='transport, clientOptions: new McpClientOptions { ProtocolVersion = Environment.GetEnvironmentVariable("MCP_PROBE_LEGACY") == "1" ? "2025-11-25" : null }, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);'
    assert section.count(old)==1
    auth.write_text(content[:start]+section.replace(old,new)+content[end:])
    command(['git','diff','--','tests'],repo,f'{label}-fixture-only.patch')
    for tfm in ['net9.0','net10.0']:
        if command(['dotnet','build',PROJECT,'-c','Release','-f',tfm],repo,f'{label}-{tfm}-probe-build.log'):
            raise SystemExit('Probe build failed; inspect evidence')
        for phase,delay,legacy in [('below-probe',1000,False),('above-probe',6500,False),('legacy-control',6500,True)]:
            for repeat in range(2):test(repo,label,tfm,phase,delay,legacy,repeat)
if not all(r['matched'] for r in rows):raise SystemExit('Observed outcome differs from proposed mechanism; inspect all failures')
