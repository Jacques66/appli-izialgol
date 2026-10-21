{ Files of records: an external file (program parameter "stock", a file of
  the working directory) is written with the buffer variable and put, then
  read back with get; an internal file of integers is written with write
  and read with read. }
program inventory(output, stock);
type item = record
               code: integer;
               name: alfa;
               price: real;
               quantity: integer
            end;
var stock: file of item;
    temp: file of integer;
    it: item;
    total: real;
    n, i: integer;

procedure add(c: integer; nm: alfa; p: real; q: integer);
begin
   stock^.code := c; stock^.name := nm; stock^.price := p; stock^.quantity := q;
   put(stock)
end;

begin
   rewrite(stock);
   add(101, 'bolts     ', 0.12, 500);
   add(102, 'nuts      ', 0.08, 750);
   add(205, 'hinges    ', 2.35, 40);
   add(310, 'locks     ', 12.90, 12);
   reset(stock);
   total := 0; n := 0;
   writeln('code  name         price  quantity      value');
   while not eof(stock) do
   begin
      it := stock^;
      with it do
      begin
         writeln(code:4, '  ', name, price:8:2, quantity:10, price * quantity:11:2);
         total := total + price * quantity
      end;
      n := n + 1;
      get(stock)
   end;
   writeln(n:1, ' items, total value ', total:1:2);
   { internal file: write and read }
   rewrite(temp);
   for i := 1 to 6 do write(temp, i * i);
   reset(temp);
   n := 0;
   while not eof(temp) do begin read(temp, i); n := n + i end;
   writeln('sum of the squares 1..6 = ', n:1)
end.
