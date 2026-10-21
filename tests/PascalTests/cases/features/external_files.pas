{ program parameter files are files of the working directory: "data" is
  provided by the test (external_files.data), "result" is written. }
program external(output, data, result);
var data, result: text; n, sum, count: integer;
begin
   reset(data); sum := 0; count := 0;
   while not eof(data) do
   begin
      while not eoln(data) do begin read(data, n); sum := sum + n; count := count + 1 end;
      readln(data)
   end;
   writeln(count:1, ' numbers, sum ', sum:1);
   rewrite(result); writeln(result, 'sum = ', sum:1); writeln(result, 'count = ', count:1);
   reset(result);
   while not eof(result) do
   begin
      while not eoln(result) do begin write(result^); get(result) end;
      readln(result); writeln
   end
end.
