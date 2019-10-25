﻿// <auto-generated />
using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PapyrusCs.Database;

namespace PapyrusCs.Migrations
{
    [DbContext(typeof(PapyrusContext))]
    [Migration("20190801161220_chunksperdimension")]
    partial class chunksperdimension
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "3.0.0-preview5.19227.1");

            modelBuilder.Entity("PapyrusCs.Database.Checksum", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd();

                    b.Property<uint>("Crc32");

                    b.Property<byte[]>("LevelDbKey");

                    b.Property<string>("Profile");

                    b.HasKey("Id");

                    b.HasIndex("LevelDbKey");

                    b.ToTable("Checksums");
                });

            modelBuilder.Entity("PapyrusCs.Database.Settings", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd();

                    b.Property<int>("ChunksPerDimension");

                    b.Property<int>("Dimension");

                    b.Property<string>("Format");

                    b.Property<int>("MaxZoom");

                    b.Property<int>("MinZoom");

                    b.Property<string>("Profile");

                    b.Property<int>("Quality");

                    b.HasKey("Id");

                    b.ToTable("Settings");
                });
#pragma warning restore 612, 618
        }
    }
}
