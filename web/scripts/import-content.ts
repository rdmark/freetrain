const result = await new Deno.Command('python3', { args: ['../scripts/import-web-content.py', ...Deno.args], stdout: 'inherit', stderr: 'inherit' }).output();
if (!result.success) Deno.exit(result.code);
