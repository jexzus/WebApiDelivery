using Microsoft.EntityFrameworkCore;
using webapiDelivery.Models;
using WebApiDelivery.Models;

namespace WebApiDelivery.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<Usuario> Usuarios { get; set; }
        public DbSet<Cliente> Clientes { get; set; }
        public DbSet<Producto> Productos { get; set; }
        public DbSet<Pedido> Pedidos { get; set; }
        public DbSet<DetallePedido> DetallesPedido { get; set; }
        public DbSet<Vendedor> Vendedores { get; set; }
        public DbSet<Repartidor> Repartidores { get; set; }
        public DbSet<PreRegistroToken> PreRegistros { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Usuario>(e =>
            {
                e.ToTable("Usuario");
                e.HasKey(u => u.Id);
                e.Property(u => u.NombreUsuario).HasMaxLength(100).IsUnicode(false);
                e.Property(u => u.Contraseña).HasMaxLength(100).IsUnicode(false);
                e.Property(u => u.Rol).HasMaxLength(20).IsUnicode(false);
            });

            modelBuilder.Entity<Cliente>(e =>
            {
                e.ToTable("Cliente");
                e.HasKey(c => c.IdCliente);
                e.Property(c => c.Nombre).HasMaxLength(50).IsUnicode(false);
                e.Property(c => c.Apellido).HasMaxLength(50).IsUnicode(false);
                e.Property(c => c.NumTelefono).HasMaxLength(20).IsUnicode(false);
                e.Property(c => c.Domicilio).HasMaxLength(100).IsUnicode(false);
                e.Property(c => c.Email).HasMaxLength(200).IsUnicode(false);
                e.HasOne(c => c.UsuarioNavigation)
                 .WithMany()
                 .HasForeignKey(c => c.IdUsuario)
                 .HasConstraintName("FK_Cliente_Usuario");
            });

            modelBuilder.Entity<Producto>(e =>
            {
                e.ToTable("Producto");
                e.HasKey(p => p.IdProducto);
                e.Property(p => p.NombreProducto).HasMaxLength(100).IsUnicode(false);
                e.Property(p => p.Descripcion).HasColumnType("text");
                e.Property(p => p.Precio).HasColumnType("decimal(10,2)");
                e.Property(p => p.Imagen).HasMaxLength(255).IsUnicode(false);
                e.Ignore(p => p.ImageUrl);
            });

            modelBuilder.Entity<Pedido>(e =>
            {
                e.ToTable("Pedido");
                e.HasKey(p => p.NumPedido);
                e.Property(p => p.FechaPedido).HasColumnType("datetime");
                e.Property(p => p.EstadoPedido).HasMaxLength(20).IsUnicode(false).HasDefaultValue("");
                e.Property(p => p.MontoTotal).HasColumnType("decimal(18,2)");
                e.Property(p => p.Observaciones).HasColumnType("text");
                e.Property(p => p.EstadoPago).HasDefaultValue("");
                e.HasOne(p => p.IdClienteNavigation)
                 .WithMany()
                 .HasForeignKey(p => p.IdCliente)
                 .HasConstraintName("FK_Pedido_Cliente")
                 .OnDelete(DeleteBehavior.SetNull);
                e.HasOne(p => p.RepartidorNavigation)
                 .WithMany()
                 .HasForeignKey(p => p.IdRepartidor)
                 .HasConstraintName("FK_Pedido_Repartidores")
                 .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<DetallePedido>(e =>
            {
                e.ToTable("DetallePedido");
                e.HasKey(d => d.IdDetalle);
                e.Property(d => d.PrecioUnitario).HasColumnType("decimal(10,2)");
                e.HasOne(d => d.PedidoNavigation)
                 .WithMany(p => p.DetallePedidos)
                 .HasForeignKey(d => d.NumPedido)
                 .HasConstraintName("FK_DetallePedido_Pedido");
                e.HasOne(d => d.IdProductoNavigation)
                 .WithMany()
                 .HasForeignKey(d => d.IdProducto)
                 .HasConstraintName("FK_DetallePedido_Producto");
            });

            modelBuilder.Entity<Vendedor>(e =>
            {
                e.ToTable("Vendedores");
                e.HasKey(v => v.Id);
                e.HasOne(v => v.Usuario)
                 .WithMany()
                 .HasForeignKey(v => v.IdUsuario)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Repartidor>(e =>
            {
                e.ToTable("Repartidores");
                e.HasKey(r => r.Id);
                e.Property(r => r.Disponibilidad).HasDefaultValue("");
                e.HasOne(r => r.Usuario)
                 .WithMany()
                 .HasForeignKey(r => r.IdUsuario)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<PreRegistroToken>(e =>
            {
                e.ToTable("PreRegistros");
                e.HasKey(p => p.Id);
                e.Property(p => p.Email).HasMaxLength(200).IsRequired();
                e.Property(p => p.TokenHash).HasMaxLength(200).IsRequired();
                e.Property(p => p.Tipo).HasMaxLength(20).HasDefaultValue("registro");
                e.Property(p => p.Usado).HasColumnType("tinyint(1)").ValueGeneratedNever();
                e.Property(p => p.CreatedAt).HasColumnType("datetime(6)").HasDefaultValueSql("UTC_TIMESTAMP(6)");
                e.Property(p => p.ExpiresAt).HasColumnType("datetime(6)");
                e.HasIndex(p => new { p.Email, p.Tipo, p.Usado });
            });
        }
    }
}
